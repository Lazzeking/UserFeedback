using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>
/// Grabs the next finished frame, the same way the vanilla F12 screenshot does: at the Done stage, from the default framebuffer.
/// Taken before the feedback dialog opens, so the picture shows the game and not the dialog.
/// </summary>
public class ScreenshotCapture : IRenderer
{
    private readonly ICoreClientAPI capi;
    private Action<BitmapRef?>? pending;

    public double RenderOrder => 1.5; // after the gui (0.1), before the vanilla screenshot (2)
    public int RenderRange => 0;

    public ScreenshotCapture(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Done, Core.ModId + "-screenshot");
    }

    /// <summary>Calls onDone on the main thread with the next frame, or null if grabbing failed. The receiver owns and disposes the bitmap.</summary>
    public void Request(Action<BitmapRef?> onDone)
    {
        pending = onDone;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (pending == null) return;

        Action<BitmapRef?> onDone = pending;
        pending = null;

        BitmapRef? bitmap = null;
        FrameBufferRef previous = capi.Render.CurrentFrameBuffer;
        try
        {
            capi.Render.CurrentFrameBuffer = null; // the window itself
            bitmap = capi.Render.GrabScreenshot(capi.Render.FrameWidth, capi.Render.FrameHeight, false, true);
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] Could not take the screenshot: {1}", Core.ModId, e.Message);
        }
        finally
        {
            capi.Render.CurrentFrameBuffer = previous;
        }

        onDone(bitmap);
    }

    public void Dispose()
    {
    }
}
