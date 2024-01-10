using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace _2_Instance
{
    internal static unsafe class Program
    {
        private static IWindow window;
        private static WebGPU wgpu;
        private static Instance* instance;
        private static Surface* surface;
        static void Main(string[] args)
        {
            var options = WindowOptions.Default;
            options.API = GraphicsAPI.None;
            options.Size = new Vector2D<int>(1280, 720);
            options.FramesPerSecond = 60;
            options.UpdatesPerSecond = 60;
            options.Position = new Vector2D<int>(100, 100);
            options.Title = "WebGPU Triangle";
            options.IsVisible = true;
            options.ShouldSwapAutomatically = false;
            options.IsContextControlDisabled = true;

            window = Window.Create(options);
            window.Load += WindowOnLoad;
            window.Update += WindowOnUpdate;
            window.Render += WindowOnRender;
            window.FramebufferResize += WindowOnFramebufferResize;
            window.Closing += WindowOnClosing;
            window.Run();
        }

        private static void WindowOnLoad()
        {
            wgpu = WebGPU.GetApi();
            InstanceDescriptor instanceDescriptor = new InstanceDescriptor();
            instance = wgpu.CreateInstance(&instanceDescriptor);
            if (instance == null)
            {
                throw new Exception("Could not initialize WebGPU!");
            }
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

        private static void WindowOnClosing()
        {
            wgpu.InstanceRelease(instance);
        }

    }
}
