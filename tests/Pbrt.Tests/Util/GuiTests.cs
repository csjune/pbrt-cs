// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests of the interactive mode's camera controls (pbrt has none). They
// use GUI's window-less constructor; the window itself was compared with
// pbrt's on an Xvfb display.

using Silk.NET.GLFW;

namespace Pbrt.Tests;

public class GuiTests
{
    private static GUI MakeGUI() =>
        new(new Vector2i(4, 3), new Bounds3f(new Point3f(0, 0, 0), new Point3f(0, 0, 1000)));

    private static void Tap(GUI gui, Keys key, KeyModifiers mods = 0)
    {
        gui.KeyboardCallback(key, InputAction.Press, mods, false);
        gui.process();
        gui.KeyboardCallback(key, InputAction.Release, mods, false);
    }

    [Fact]
    public void HeldKeysMoveTheCamera()
    {
        GUI gui = MakeGUI();
        Assert.Equal(12, gui.MapFramebuffer().Length);
        Assert.False(gui.process());

        // The move scale is the scene's diagonal over 1000; the camera
        // moves each frame while the key is down.
        gui.KeyboardCallback(Keys.W, InputAction.Press, 0, false);
        Assert.True(gui.process());
        Assert.True(gui.process());
        gui.KeyboardCallback(Keys.W, InputAction.Release, 0, false);
        Assert.False(gui.process());
        Assert.Equal(new Point3f(0, 0, 2), gui.GetCameraTransform().Apply(new Point3f(0, 0, 0)));

        // '=' doubles the move scale (once per press).
        Tap(gui, Keys.Equal);
        gui.KeyboardCallback(Keys.A, InputAction.Press, 0, false);
        gui.process();
        gui.KeyboardCallback(Keys.A, InputAction.Release, 0, false);
        Assert.Equal(new Point3f(-2, 0, 2), gui.GetCameraTransform().Apply(new Point3f(0, 0, 0)));

        // Arrow keys rotate by half a degree per frame; 'r' resets.
        gui.KeyboardCallback(Keys.Right, InputAction.Press, 0, false);
        gui.process();
        gui.KeyboardCallback(Keys.Right, InputAction.Release, 0, false);
        Assert.Equal(Translate(new Vector3f(0, 0, 2)) * Translate(new Vector3f(-2, 0, 0)) *
                     Rotate(.5f, new Vector3f(0, 1, 0)), gui.GetCameraTransform());
        gui.KeyboardCallback(Keys.R, InputAction.Press, 0, false);
        Assert.True(gui.process());
        Assert.Equal(new Transform(), gui.GetCameraTransform());
    }

    [Fact]
    public void ExposureAndPrint()
    {
        GUI gui = MakeGUI();
        Tap(gui, Keys.B);
        Assert.Equal(1.125f, gui.exposure);
        // Holding 'b' only counts once.
        gui.KeyboardCallback(Keys.B, InputAction.Press, 0, false);
        Assert.False(gui.process());
        Assert.False(gui.process());
        gui.KeyboardCallback(Keys.B, InputAction.Release, 0, false);
        Assert.Equal(1.125f * 1.125f, gui.exposure);
        Tap(gui, Keys.B, KeyModifiers.Shift);
        Assert.Equal(1.125f, gui.exposure);

        Assert.False(gui.printCameraTransform);
        Tap(gui, Keys.C);
        Assert.True(gui.printCameraTransform);
    }

    [Fact]
    public void MouseDragRotates()
    {
        GUI gui = MakeGUI();
        // Moving without the button down does nothing.
        gui.CursorPosCallback(10, 0);
        Assert.False(gui.process());

        gui.MouseButtonCallback(MouseButton.Left, InputAction.Press, 10, 0);
        gui.CursorPosCallback(15, 0);
        Assert.True(gui.process());
        Assert.Equal(Rotate(1, new Vector3f(0, 1, 0)), gui.GetCameraTransform());
        gui.CursorPosCallback(15, 3);
        Assert.True(gui.process());
        Assert.Equal(Rotate(1, new Vector3f(0, 1, 0)) * Rotate(1, new Vector3f(1, 0, 0)),
                     gui.GetCameraTransform());

        gui.MouseButtonCallback(MouseButton.Left, InputAction.Release, 0, 0);
        gui.CursorPosCallback(30, 30);
        Assert.False(gui.process());
    }
}
