// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/gui.h and gui.cpp
//
// The window for --interactive. GLFW and OpenGL come from Silk.NET (its
// GLFW package carries the native GLFW libraries) in place of pbrt's
// bundled GLFW and glad; as in pbrt, the image is drawn with the legacy
// glDrawPixels() (pbrt sets its core-profile window hints after creating
// the window, so they have no effect). The callbacks are closures instead
// of going through the window's user pointer. The CUDA framebuffer isn't
// ported. Recorded frames (Ctrl-R) have the image's resolution; pbrt
// uses the window's framebuffer size, which only differs with display
// scaling, where it copies the wrong number of pixels.

using Silk.NET.GLFW;
using Silk.NET.OpenGL.Legacy;
using GLPixelFormat = Silk.NET.OpenGL.Legacy.PixelFormat;
using Monitor = Silk.NET.GLFW.Monitor;

namespace Pbrt;

public enum DisplayState
{
    EXIT,
    RESET,
    NONE
}

public sealed unsafe class GUI : IDisposable
{
    private static Glfw? glfw;

    // It's a little messy that the state of values controlled via the UI
    // are just public variables here but it's probably not worth putting
    // an abstraction layer on top of all this at this point.
    public Transform GetCameraTransform() => movingFromCamera;
    public Float exposure = 1f;
    public bool printCameraTransform = false;

    private readonly HashSet<char> keysDown = [];
    private Float moveScale = 1f;
    private Transform movingFromCamera = new();
    private readonly Vector2i resolution;
    private bool recordFrames = false;
    private int frameNumber = 0;
    private bool pressed = false;
    private Float xoffset = 0f;
    private Float yoffset = 0f;
    private double lastX = 0f;
    private double lastY = 0f;

    private readonly RGB[] cpuFramebuffer;
    private readonly WindowHandle* window;
    private readonly GL? gl;

    // (Kept so that the delegates GLFW calls aren't collected.)
    private readonly GlfwCallbacks.KeyCallback? keyCallback;
    private readonly GlfwCallbacks.MouseButtonCallback? mouseButtonCallback;
    private readonly GlfwCallbacks.CursorPosCallback? cursorPosCallback;
    private static readonly GlfwCallbacks.ErrorCallback errorCallback =
        (error, desc) => LOG_ERROR($"GLFW [{(int)error}]: {desc}");

    public RGB[] MapFramebuffer() => cpuFramebuffer;

    public void UnmapFramebuffer() { }

    private static string getGLErrorString(GLEnum error) => error switch
    {
        GLEnum.NoError => "No error",
        GLEnum.InvalidEnum => "Invalid enum",
        GLEnum.InvalidValue => "Invalid value",
        GLEnum.InvalidOperation => "Invalid operation",
        GLEnum.OutOfMemory => "Out of memory",
        _ => "Unknown GL error"
    };

    private void GL_CHECK(string call)
    {
        if (gl!.GetError() is GLEnum err && err != GLEnum.NoError)
            LOG_FATAL($"GL error: {getGLErrorString(err)} for {call}");
    }

    public static void Initialize()
    {
        glfw = Glfw.GetApi();
        if (!glfw.Init())
            LOG_FATAL("Unable to initialize GLFW");
    }

    public static Point2i GetResolution()
    {
        Monitor* monitor = glfw!.GetPrimaryMonitor();
        VideoMode* videoMode = glfw.GetVideoMode(monitor);
        return new Point2i(videoMode->Width, videoMode->Height);
    }

    public GUI(string title, Vector2i resolution, Bounds3f sceneBounds)
        : this(resolution, sceneBounds)
    {
        glfw!.SetErrorCallback(errorCallback);
        if (Options.fullscreen)
            window = glfw.CreateWindow(resolution.x, resolution.y, "pbrt", glfw.GetPrimaryMonitor(), null);
        else
            window = glfw.CreateWindow(resolution.x, resolution.y, "pbrt", null, null);

        if (window == null)
        {
            glfw.Terminate();
            LOG_FATAL("Unable to create GLFW window");
        }
        keyCallback = (w, key, scan, action, mods) =>
            KeyboardCallback(key, action, mods, glfw.GetKey(w, Keys.ControlLeft) == (int)InputAction.Press);
        mouseButtonCallback = (w, button, action, mods) =>
        {
            double x = 0, y = 0;
            if (button == MouseButton.Left && action == InputAction.Press)
                glfw.GetCursorPos(w, out x, out y);
            MouseButtonCallback(button, action, x, y);
        };
        cursorPosCallback = (w, xpos, ypos) => CursorPosCallback(xpos, ypos);
        glfw.SetKeyCallback(window, keyCallback);
        glfw.SetMouseButtonCallback(window, mouseButtonCallback);
        glfw.SetCursorPosCallback(window, cursorPosCallback);

        glfw.MakeContextCurrent(window);

        glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(WindowHintInt.ContextVersionMinor, 2);
        glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
        glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

        gl = GL.GetApi(name => glfw.GetProcAddress(name));
        if (gl == null)
            LOG_FATAL("gladLoadGLLoader failed");
    }

    // (The window-independent part of the constructor; tests use it to
    // check the camera controls.)
    internal GUI(Vector2i resolution, Bounds3f sceneBounds)
    {
        this.resolution = resolution;
        moveScale = Length(sceneBounds.Diagonal()) / 1000f;
        cpuFramebuffer = new RGB[resolution.x * resolution.y];
    }

    public void Dispose()
    {
        if (window != null)
        {
            glfw!.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    internal void KeyboardCallback(Keys key, InputAction action, KeyModifiers mods, bool leftControlDown)
    {
        if (key == Keys.Escape && action == InputAction.Press && window != null)
            glfw!.SetWindowShouldClose(window, true);

        void doKey(Keys k, char ch)
        {
            if (key == k)
            {
                if (action == InputAction.Press)
                    keysDown.Add(ch);
                else if (action == InputAction.Release)
                    keysDown.Remove(ch);
            }
        }

        doKey(Keys.A, 'a');
        doKey(Keys.D, 'd');
        doKey(Keys.S, 's');
        doKey(Keys.W, 'w');
        doKey(Keys.Q, 'q');
        doKey(Keys.E, 'e');

        doKey(Keys.B, (mods & KeyModifiers.Shift) != 0 ? 'B' : 'b');
        doKey(Keys.C, 'c');
        doKey(Keys.Equal, '=');
        doKey(Keys.Minus, '-');

        doKey(Keys.Left, 'L');
        doKey(Keys.Right, 'R');
        doKey(Keys.Up, 'U');
        doKey(Keys.Down, 'D');

        if (key == Keys.R && action == InputAction.Press && leftControlDown)
            recordFrames = !recordFrames;
        else
            doKey(Keys.R, 'r');
    }

    private bool processMouse()
    {
        bool needsReset = false;
        Float amount = 1f;
        if (!pressed)
            return false;
        if (xoffset < 0)
        {
            movingFromCamera = movingFromCamera * Rotate(-amount, new Vector3f(0, 1, 0));
            needsReset = true;
            xoffset = 0;
        }
        if (xoffset > 0)
        {
            movingFromCamera = movingFromCamera * Rotate(amount, new Vector3f(0, 1, 0));
            needsReset = true;
            xoffset = 0;
        }
        if (yoffset > 0)
        {
            movingFromCamera = movingFromCamera * Rotate(-amount, new Vector3f(1, 0, 0));
            needsReset = true;
            yoffset = 0;
        }
        if (yoffset < 0)
        {
            movingFromCamera = movingFromCamera * Rotate(amount, new Vector3f(1, 0, 0));
            needsReset = true;
            yoffset = 0;
        }
        return needsReset;
    }

    internal bool process()
    {
        bool needsReset = false;
        needsReset |= processKeys();
        needsReset |= processMouse();
        return needsReset;
    }

    private bool processKeys()
    {
        bool needsReset = false;

        void handleNeedsReset(char key, Func<Transform, Transform> update)
        {
            if (keysDown.Contains(key))
            {
                movingFromCamera = update(movingFromCamera);
                needsReset = true;
            }
        }

        handleNeedsReset('a', t => t * Translate(new Vector3f(-moveScale, 0, 0)));
        handleNeedsReset('d', t => t * Translate(new Vector3f(moveScale, 0, 0)));
        handleNeedsReset('s', t => t * Translate(new Vector3f(0, 0, -moveScale)));
        handleNeedsReset('w', t => t * Translate(new Vector3f(0, 0, moveScale)));
        handleNeedsReset('q', t => t * Translate(new Vector3f(0, -moveScale, 0)));
        handleNeedsReset('e', t => t * Translate(new Vector3f(0, moveScale, 0)));
        handleNeedsReset('L', t => t * Rotate(-.5f, new Vector3f(0, 1, 0)));
        handleNeedsReset('R', t => t * Rotate(.5f, new Vector3f(0, 1, 0)));
        handleNeedsReset('U', t => t * Rotate(-.5f, new Vector3f(1, 0, 0)));
        handleNeedsReset('D', t => t * Rotate(.5f, new Vector3f(1, 0, 0)));
        handleNeedsReset('r', t => new Transform());

        // No reset needed for these.
        if (keysDown.Remove('c'))
            printCameraTransform = true;
        if (keysDown.Remove('b'))
            exposure *= 1.125f;
        if (keysDown.Remove('B'))
            exposure /= 1.125f;
        if (keysDown.Remove('='))
            moveScale *= 2;
        if (keysDown.Remove('-'))
            moveScale *= 0.5f;

        return needsReset;
    }

    internal void MouseButtonCallback(MouseButton button, InputAction action, double cursorX, double cursorY)
    {
        if (button == MouseButton.Left && action == InputAction.Press)
        {
            pressed = true;
            lastX = cursorX;
            lastY = cursorY;
        }
        if (button == MouseButton.Left && action == InputAction.Release)
            pressed = false;
    }

    internal void CursorPosCallback(double xpos, double ypos)
    {
        xoffset = (Float)(xpos - lastX);
        yoffset = (Float)(lastY - ypos);
        lastX = xpos;
        lastY = ypos;
    }

    public DisplayState RefreshDisplay()
    {
        glfw!.GetFramebufferSize(window, out int width, out int height);
        glfw.GetWindowSize(window, out int windowWidth, out int windowHeight);
        gl!.Viewport(0, 0, (uint)width, (uint)height);
        GL_CHECK("glViewport(0, 0, width, height)");
        float[] pixelScales = [(float)width / windowWidth, (float)height / windowHeight];

        // (These are deprecated, but pbrt uses them.)
#pragma warning disable CS0618
        gl.Enable(EnableCap.FramebufferSrgb);
        GL_CHECK("glEnable(GL_FRAMEBUFFER_SRGB)");
        gl.RasterPos2(-1f, 1f);
        GL_CHECK("glRasterPos2f(-1, 1)");
        gl.PixelZoom(pixelScales[0], -pixelScales[1]);
        GL_CHECK("glPixelZoom(pixelScales[0], -pixelScales[1])");
        fixed (RGB* fb = cpuFramebuffer)
            gl.DrawPixels((uint)resolution.x, (uint)resolution.y, GLPixelFormat.Rgb, PixelType.Float, fb);
#pragma warning restore CS0618
        GL_CHECK("glDrawPixels(resolution.x, resolution.y, GL_RGB, GL_FLOAT, cpuFramebuffer)");

        glfw.SwapBuffers(window);
        glfw.PollEvents();

        if (recordFrames)
        {
            Image image = new(PixelFormat.Float, new Point2i(resolution.x, resolution.y), ["R", "G", "B"]);
            for (int y = 0; y < resolution.y; ++y)
                for (int x = 0; x < resolution.x; ++x)
                {
                    RGB rgb = cpuFramebuffer[x + y * resolution.x];
                    for (int c = 0; c < 3; ++c)
                        image.SetChannel(new Point2i(x, y), c, rgb[c]);
                }

            int fn = frameNumber;
            RunAsync(() =>
            {
                // TODO: set metadata for e.g. current camera position...
                ImageMetadata metadata = new();
                image.Write($"pbrt-frame{fn:D5}.exr", metadata);
                return 0;
            });

            ++frameNumber;
        }

        if (glfw.WindowShouldClose(window))
            return DisplayState.EXIT;
        else if (process())
            return DisplayState.RESET;
        else
            return DisplayState.NONE;
    }
}
