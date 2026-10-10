// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the display server client (pbrt has none): a local TCP
// listener plays tev and decodes the messages.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pbrt.Tests;

// (ErrorExit() disconnects from the display server, so these don't run in
// parallel with tests that call it.)
[Collection("Options")]
public class DisplayTests
{
    private sealed record Message(int type, byte grabFocus, string title, byte[] data, int offset);

    private static string ReadString(byte[] m, ref int offset)
    {
        int end = Array.IndexOf(m, (byte)0, offset);
        string s = Encoding.UTF8.GetString(m, offset, end - offset);
        offset = end + 1;
        return s;
    }

    private static List<Message> Receive(Action display)
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<byte[]> received = Task.Run(() =>
        {
            using TcpClient client = listener.AcceptTcpClient();
            MemoryStream ms = new();
            client.GetStream().CopyTo(ms);
            return ms.ToArray();
        });

        ConnectToDisplayServer($"127.0.0.1:{port}");
        display();
        DisconnectFromDisplayServer();

        Assert.True(received.Wait(TimeSpan.FromSeconds(30)));
        listener.Stop();
        byte[] data = received.Result;

        List<Message> messages = [];
        for (int o = 0; o < data.Length;)
        {
            int length = BitConverter.ToInt32(data, o);
            byte[] m = data[o..(o + length)];
            o += length;
            int offset = 6;
            string title = ReadString(m, ref offset);
            messages.Add(new Message(m[4], m[5], title, m, offset));
        }
        return messages;
    }

    [Fact]
    public void StaticImage()
    {
        // Two tiles across.
        Image image = new(PixelFormat.Float, new Point2i(130, 3), ["R", "G", "B"]);
        foreach (Point2i p in new Bounds2i(new Point2i(0, 0), image.Resolution()))
            for (int c = 0; c < 3; ++c)
                image.SetChannel(p, c, p.x + 1000 * p.y + 0.25f * c);

        List<Message> messages = Receive(() => DisplayStatic("static", image));

        string title = $"static ({Environment.ProcessId})";
        Assert.Equal(1 + 2 * 3, messages.Count);

        Message open = messages[0];
        Assert.Equal(4, open.type);  // CreateImage
        Assert.Equal(1, open.grabFocus);
        Assert.Equal(title, open.title);
        int offset = open.offset;
        Assert.Equal(130, BitConverter.ToInt32(open.data, offset));
        Assert.Equal(3, BitConverter.ToInt32(open.data, offset + 4));
        Assert.Equal(3, BitConverter.ToInt32(open.data, offset + 8));
        offset += 12;
        Assert.Equal("R", ReadString(open.data, ref offset));
        Assert.Equal("G", ReadString(open.data, ref offset));
        Assert.Equal("B", ReadString(open.data, ref offset));
        Assert.Equal(open.data.Length, offset);

        int[] tileX = [0, 128], tileWidth = [128, 2];
        for (int i = 0; i < 6; ++i)
        {
            Message update = messages[1 + i];
            Assert.Equal(3, update.type);  // UpdateImage
            Assert.Equal(0, update.grabFocus);
            Assert.Equal(title, update.title);
            offset = update.offset;
            int c = i % 3, tile = i / 3;
            Assert.Equal("RGB"[c].ToString(), ReadString(update.data, ref offset));
            int x = BitConverter.ToInt32(update.data, offset), y = BitConverter.ToInt32(update.data, offset + 4);
            int w = BitConverter.ToInt32(update.data, offset + 8), h = BitConverter.ToInt32(update.data, offset + 12);
            offset += 16;
            Assert.Equal((tileX[tile], 0, tileWidth[tile], 3), (x, y, w, h));
            Assert.Equal(offset + w * h * 4, update.data.Length);
            for (int py = 0; py < h; ++py)
                for (int px = 0; px < w; ++px)
                    Assert.Equal(x + px + 1000 * py + 0.25f * c,
                                 BitConverter.ToSingle(update.data, offset + 4 * (px + py * w)));
        }
    }

    [Fact]
    public void DynamicSkipsUnchangedTiles()
    {
        // The second tile stays zero, which the viewer already has.
        float[] values = new float[200 * 2];
        for (int i = 0; i < 128; ++i)
            values[i] = values[200 + i] = 1;

        List<Message> messages = Receive(() => DisplayDynamic("dynamic", values, 200));

        Assert.Equal(4, messages[0].type);
        Assert.All(messages.Skip(1), m => Assert.Equal(3, m.type));
        // (The update thread may display it more than once, but unchanged
        // tiles are only sent the first time.)
        Assert.Equal(2, messages.Count);
        int offset = messages[1].offset;
        Assert.Equal("value", ReadString(messages[1].data, ref offset));
        Assert.Equal(0, BitConverter.ToInt32(messages[1].data, offset));
    }

    [Fact]
    public void NotListening()
    {
        // Rendering goes on when nothing is listening.
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        ConnectToDisplayServer($"127.0.0.1:{port}");
        DisplayDynamic("nothing", new float[4], 2);
        DisconnectFromDisplayServer();
    }

    [Fact]
    public void BadAddress()
    {
        Assert.Throws<FatalErrorException>(() => ConnectToDisplayServer("localhost"));
    }
}
