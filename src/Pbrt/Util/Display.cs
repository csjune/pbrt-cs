// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/display.h and display.cpp
//
// Sends images to a display server such as tev (--display-server
// host:port) over TCP, using tev's IPC protocol. Sockets are .NET's
// System.Net.Sockets (which doesn't raise SIGPIPE), so there's no WinSock
// setup. The getTileValues callback gets one float[tileSize * tileSize]
// per channel instead of a span of spans into the message buffers; the
// values are copied into the message after it returns. The image title
// gets the process id on all platforms (pbrt uses the thread id on
// Windows). pbrt's templated span overloads are the float[] ones below.

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Pbrt;

public delegate void DisplayGetValues(Bounds2i b, float[][] displayValues);

public static partial class Util
{
    private sealed class IPCChannel : IDisposable
    {
        private readonly string address, port;
        private Socket? socket;

        public IPCChannel(string hostname)
        {
            int split = hostname.LastIndexOf(':');
            if (split < 0)
                ErrorExit($"Expected \"host:port\" for display server address. Given \"{hostname}\".");
            address = hostname[..split];
            port = hostname[(split + 1)..];

            Connect();
        }

        public bool Connected() => socket != null;

        private void Connect()
        {
            CHECK(socket == null);

            LOG_VERBOSE($"Trying to connect to display server");

            if (!int.TryParse(port, out int portNumber) || portNumber < 0 || portNumber > 65535)
                ErrorExit("Servname not supported for ai_socktype");
            IPAddress[] addresses;
            try
            {
                // (getaddrinfo() also takes a numeric address in brackets.)
                string host = address.StartsWith('[') && address.EndsWith(']') ? address[1..^1] : address;
                addresses = Dns.GetHostAddresses(host);
            }
            catch (SocketException e)
            {
                ErrorExit(e.Message);
                return;
            }

            foreach (IPAddress addr in addresses)
            {
                Socket s;
                try
                {
                    s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                }
                catch (SocketException e)
                {
                    LOG_VERBOSE($"socket() failed: {e.Message}");
                    continue;
                }

                s.SendTimeout = 3000;

                try
                {
                    s.Connect(new IPEndPoint(addr, portNumber));
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.ConnectionRefused)
                        LOG_VERBOSE($"Connection refused. Will try again...");
                    else
                        LOG_VERBOSE($"connect() failed: {e.Message}");

                    s.Dispose();
                    continue;
                }

                socket = s;
                break;  // success
            }

            if (socket != null)
                LOG_VERBOSE($"Connected to display server");
        }

        public void Dispose()
        {
            if (Connected())
                Disconnect();
        }

        private void Disconnect()
        {
            CHECK(Connected());

            socket!.Dispose();
            socket = null;
        }

        public bool Send(byte[] message, int length)
        {
            if (!Connected())
            {
                Connect();
                if (!Connected())
                    return false;
            }

            // Start with the length of the message.
            BitConverter.TryWriteBytes(message.AsSpan(0, 4), length);

            string err;
            try
            {
                int bytesSent = socket!.Send(message, 0, length, SocketFlags.None);
                if (bytesSent == length)
                    return true;
                err = "short write";
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                err = e.Message;
            }

            LOG_ERROR($"send to display server failed: {err}");
            Disconnect();
            return false;
        }
    }

    private enum DisplayDirective : byte
    {
        OpenImage = 0,
        ReloadImage = 1,
        CloseImage = 2,
        UpdateImage = 3,
        CreateImage = 4,
    }

    private sealed class DisplayMessageWriter(byte[] buffer)
    {
        public int Offset;

        public void Write(string s)
        {
            Offset += Encoding.UTF8.GetBytes(s, buffer.AsSpan(Offset));
            buffer[Offset++] = 0;
        }

        public void Write(byte v) => buffer[Offset++] = v;

        public void Write(int v)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(Offset), v);
            Offset += 4;
        }
    }

    private const int displayTileSize = 128;

    private sealed class DisplayItem
    {
        private bool openedImage = false;
        private readonly string title;
        private readonly Point2i resolution;
        private readonly DisplayGetValues getTileValues;
        private readonly string[] channelNames;
        private readonly ImageChannelBuffer[] channelBuffers;

        private sealed class ImageChannelBuffer
        {
            public readonly byte[] buffer;
            public readonly int tileBoundsOffset, channelValuesOffset;
            public readonly ulong[] tileHashes;
            public int setCount;

            public ImageChannelBuffer(string channelName, int nTiles, string title)
            {
                int bufferAlloc = displayTileSize * displayTileSize * sizeof(float) +
                                  Encoding.UTF8.GetByteCount(title) + Encoding.UTF8.GetByteCount(channelName) + 32;
                buffer = new byte[bufferAlloc];

                DisplayMessageWriter w = new(buffer);
                w.Write(0);  // reserve space for message length
                w.Write((byte)DisplayDirective.UpdateImage);
                byte grabFocus = 0;
                w.Write(grabFocus);
                w.Write(title);
                w.Write(channelName);

                tileBoundsOffset = w.Offset;
                channelValuesOffset = tileBoundsOffset + 4 * sizeof(int);

                // The color contents are zero-initialized; compute the hash for a
                // fully-zero tile (which corresponds to the initial state on the
                // viewer side.)
                ulong zeroHash = HashBuffer<byte>(Values());
                tileHashes = new ulong[nTiles];
                Array.Fill(tileHashes, zeroHash);
            }

            public Span<byte> Values() =>
                buffer.AsSpan(channelValuesOffset, displayTileSize * displayTileSize * sizeof(float));

            public void SetTileBounds(int x, int y, int width, int height)
            {
                DisplayMessageWriter w = new(buffer) { Offset = tileBoundsOffset };
                w.Write(x);
                w.Write(y);
                w.Write(width);
                w.Write(height);

                setCount = width * height;
            }

            public bool SendIfChanged(IPCChannel ipcChannel, int tileIndex, float[] values)
            {
                // (Values past setCount are left zero, so that a tile hashes
                // the same as long as its pixels do.)
                Span<byte> v = Values();
                MemoryMarshal.AsBytes(values.AsSpan(0, setCount)).CopyTo(v);
                v[(setCount * sizeof(float))..].Clear();

                ulong hash = HashBuffer<byte>(v);
                if (hash == tileHashes[tileIndex])
                    return true;

                if (!ipcChannel.Send(buffer, channelValuesOffset + setCount * sizeof(float)))
                    return false;

                tileHashes[tileIndex] = hash;
                return true;
            }
        }

        public DisplayItem(string baseTitle, Point2i resolution, string[] channelNames,
                           DisplayGetValues getTileValues)
        {
            this.resolution = resolution;
            this.getTileValues = getTileValues;
            this.channelNames = channelNames;
            title = $"{baseTitle} ({Environment.ProcessId})";

            int nTiles = ((resolution.x + displayTileSize - 1) / displayTileSize) *
                         ((resolution.y + displayTileSize - 1) / displayTileSize);

            channelBuffers = new ImageChannelBuffer[channelNames.Length];
            for (int c = 0; c < channelNames.Length; ++c)
                channelBuffers[c] = new ImageChannelBuffer(channelNames[c], nTiles, title);
        }

        public bool Display(IPCChannel ipcChannel)
        {
            if (!openedImage)
            {
                if (!SendOpenImage(ipcChannel))
                    // maybe next time
                    return false;
                openedImage = true;
            }

            float[][] displayValues = new float[channelBuffers.Length][];
            for (int c = 0; c < channelBuffers.Length; ++c)
                displayValues[c] = new float[displayTileSize * displayTileSize];

            int tileIndex = 0;
            for (int y = 0; y < resolution.y; y += displayTileSize)
                for (int x = 0; x < resolution.x; x += displayTileSize, ++tileIndex)
                {
                    int height = Math.Min(y + displayTileSize, resolution.y) - y;
                    int width = Math.Min(x + displayTileSize, resolution.x) - x;

                    for (int c = 0; c < channelBuffers.Length; ++c)
                        channelBuffers[c].SetTileBounds(x, y, width, height);

                    Bounds2i b = new(new Point2i(x, y), new Point2i(x + width, y + height));
                    getTileValues(b, displayValues);

                    // Send the RGB buffers only if they're different than
                    // the last version sent.
                    for (int c = 0; c < channelBuffers.Length; ++c)
                        if (!channelBuffers[c].SendIfChanged(ipcChannel, tileIndex, displayValues[c]))
                        {
                            // Welp. Stop for now...
                            openedImage = false;
                            return false;
                        }
                }

            return true;
        }

        private bool SendOpenImage(IPCChannel ipcChannel)
        {
            // Initial "open the image" message
            int size = 32 + Encoding.UTF8.GetByteCount(title);
            foreach (string name in channelNames)
                size += Encoding.UTF8.GetByteCount(name) + 1;
            byte[] buffer = new byte[size];
            DisplayMessageWriter w = new(buffer);

            w.Write(0);  // reserve space for message length
            w.Write((byte)DisplayDirective.CreateImage);
            byte grabFocus = 1;
            w.Write(grabFocus);
            w.Write(title);

            int nChannels = channelNames.Length;
            w.Write(resolution.x);
            w.Write(resolution.y);
            w.Write(nChannels);
            for (int c = 0; c < nChannels; ++c)
                w.Write(channelNames[c]);

            return ipcChannel.Send(buffer, w.Offset);
        }
    }

    private static volatile bool displayExitThread = false;
    private static readonly object displayMutex = new();
    private static Thread? displayUpdateThread;
    private static readonly List<DisplayItem> dynamicDisplayItems = [];

    private static IPCChannel? displayChannel;

    private static void updateDynamicItems()
    {
        while (!displayExitThread)
        {
            Thread.Sleep(250);

            lock (displayMutex)
                foreach (DisplayItem item in dynamicDisplayItems)
                    item.Display(displayChannel!);
        }

        // One last time to get the last bits
        lock (displayMutex)
        {
            foreach (DisplayItem item in dynamicDisplayItems)
                item.Display(displayChannel!);

            dynamicDisplayItems.Clear();
            displayChannel!.Dispose();
            displayChannel = null;
        }
    }

    // DisplayServer Function Definitions
    public static void ConnectToDisplayServer(string host)
    {
        CHECK(displayChannel == null);
        displayChannel = new IPCChannel(host);

        displayUpdateThread = new Thread(updateDynamicItems) { IsBackground = true, Name = "Display server" };
        displayUpdateThread.Start();
    }

    public static void DisconnectFromDisplayServer()
    {
        // (ErrorExit() calls this, possibly from a display callback.)
        if (displayUpdateThread != null && displayUpdateThread != Thread.CurrentThread)
        {
            displayExitThread = true;
            displayUpdateThread.Join();
            displayUpdateThread = null;
            displayExitThread = false;
        }
    }

    private static DisplayItem GetImageDisplayItem(string title, Image image, ImageChannelDesc? channelDesc)
    {
        void getValues(Bounds2i b, float[][] displayValues)
        {
            int offset = 0;
            foreach (Point2i p in b)
            {
                ImageChannelValues v = channelDesc.HasValue ? image.GetChannels(p, channelDesc.Value)
                                                            : image.GetChannels(p);
                for (int i = 0; i < v.Size; ++i)
                    displayValues[i][offset] = (float)v[i];
                ++offset;
            }
        }

        string[] channelNames;
        if (channelDesc.HasValue)
            channelNames = [.. image.ChannelNames(channelDesc.Value)];
        else
        {
            if (image.NChannels() == 3)
                channelNames = ["R", "G", "B"];
            else
            {
                channelNames = new string[image.NChannels()];
                for (int i = 0; i < image.NChannels(); ++i)
                    channelNames[i] = $"channel {i}";
            }
        }

        return new DisplayItem(title, image.Resolution(), channelNames, getValues);
    }

    public static void DisplayStatic(string title, Image image, ImageChannelDesc? channelDesc = null) =>
        displayStatic(GetImageDisplayItem(title, image, channelDesc), title);

    public static void DisplayDynamic(string title, Image image, ImageChannelDesc? channelDesc = null) =>
        displayDynamic(GetImageDisplayItem(title, image, channelDesc));

    public static void DisplayStatic(string title, Point2i resolution, string[] channelNames,
                                     DisplayGetValues getTileValues) =>
        displayStatic(new DisplayItem(title, resolution, channelNames, getTileValues), title);

    public static void DisplayDynamic(string title, Point2i resolution, string[] channelNames,
                                      DisplayGetValues getTileValues) =>
        displayDynamic(new DisplayItem(title, resolution, channelNames, getTileValues));

    public static void DisplayStatic(string title, float[] values, int xResolution) =>
        DisplayStatic(title, values, ["value"], xResolution);

    public static void DisplayDynamic(string title, float[] values, int xResolution) =>
        DisplayDynamic(title, values, ["value"], xResolution);

    // (values holds channelNames.Length interleaved channels per pixel.)
    public static void DisplayStatic(string title, float[] values, string[] channelNames, int xResolution) =>
        DisplayStatic(title, displayValuesResolution(values, channelNames, xResolution), channelNames,
                      displayValuesGetter(values, channelNames, xResolution));

    public static void DisplayDynamic(string title, float[] values, string[] channelNames, int xResolution) =>
        DisplayDynamic(title, displayValuesResolution(values, channelNames, xResolution), channelNames,
                       displayValuesGetter(values, channelNames, xResolution));

    private static Point2i displayValuesResolution(float[] values, string[] channelNames, int xResolution)
    {
        int nPixels = values.Length / channelNames.Length;
        CHECK_EQ(0, values.Length % channelNames.Length);
        CHECK_EQ(0, nPixels % xResolution);
        return new Point2i(xResolution, nPixels / xResolution);
    }

    private static DisplayGetValues displayValuesGetter(float[] values, string[] channelNames, int xResolution) =>
        (b, displayValue) =>
        {
            DCHECK_EQ(channelNames.Length, displayValue.Length);
            int index = 0;
            foreach (Point2i p in b)
            {
                int offset = p.x + p.y * xResolution;
                for (int i = 0; i < channelNames.Length; ++i)
                    displayValue[i][index] = values[offset * channelNames.Length + i];
                ++index;
            }
        };

    private static void displayStatic(DisplayItem item, string title)
    {
        lock (displayMutex)
            if (displayChannel == null || !item.Display(displayChannel))
                LOG_ERROR($"Unable to display static content \"{title}\".");
    }

    private static void displayDynamic(DisplayItem item)
    {
        lock (displayMutex)
            if (displayChannel != null)
                dynamicDisplayItems.Add(item);
    }
}
