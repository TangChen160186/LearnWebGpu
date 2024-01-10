using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace LearnWeGpu
{
    internal class Program
    {
        static void Main(string[] args)
        {
            WindowOptions options = new WindowOptions
            {
                API = GraphicsAPI.None,
                FramesPerSecond = 60,
                UpdatesPerSecond = 60,
                Title = "Learn WebGpu",
                Size = new Vector2D<int>(1270, 680),
                Position = new Vector2D<int>(100, 100),
                VSync = true,
                IsVisible = true,
                ShouldSwapAutomatically = true,
            };
            IWindow window = Window.Create(options);
            window.Load += WindowOnLoad;
            window.Update += WindowOnUpdate;
            window.Render += WindowOnRender;
            window.FramebufferResize += WindowOnFramebufferResize;

            window.Run();
        }

        private static void WindowOnLoad()
        {
        }

        private static void WindowOnRender(double obj)
        {
        }

        private static void WindowOnUpdate(double obj)
        {
        }

        private static void WindowOnFramebufferResize(Vector2D<int> obj)
        {
        }
    }
}