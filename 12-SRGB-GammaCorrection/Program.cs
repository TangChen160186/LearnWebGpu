using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace _12_SRGB_GammaCorrection
{
    internal static unsafe class Program
    {
        private static IWindow window;
        private static WebGPU wgpu;
        private static Instance* instance;
        private static Surface* surface;
        private static Adapter* adapter;
        private static Device* device;
        private static Queue* queue;
        private static RenderPipeline* renderPipeline;
        private static ShaderModule* shaderModule;
        private static string shaderSource = """
                                             struct VertexInput {
                                                 @location(0) position: vec2f,
                                                 @location(1) color: vec3f,
                                             };
                                             
                                             /**
                                              * A structure with fields labeled with builtins and locations can also be used
                                              * as *output* of the vertex shader, which is also the input of the fragment
                                              * shader.
                                              */
                                             struct VertexOutput {
                                                 @builtin(position) position: vec4f,
                                                 // The location here does not refer to a vertex attribute, it just means
                                                 // that this field must be handled by the rasterizer.
                                                 // (It can also refer to another field of another struct that would be used
                                                 // as input to the fragment shader.)
                                                 @location(0) color: vec3f,
                                             };
                                             
                                             @vertex
                                             fn vs_main(in: VertexInput) -> VertexOutput {
                                                 var out: VertexOutput;
                                                 out.position = vec4f(in.position, 0.0, 1.0);
                                                 out.color = in.color; // forward to the fragment shader
                                                 return out;
                                             }
                                             
                                             @fragment
                                             fn fs_main(in: VertexOutput) -> @location(0) vec4f {
                                                 // We apply a gamma-correction to the color
                                                 // We need to convert our input sRGB color into linear before the target
                                                 // surface converts it back to sRGB.
                                                 let linear_color = pow(in.color, vec3f(2.2));
                                                 return vec4f(linear_color, 1.0);
                                             }
                                             """;

        private static Buffer* vertexBuffer;
        private static float[] vertexBufferData =
        {
            // x,   y,      r,   g,   b
            -0.5F, -0.5F,   1.0F, 0.0F, 0.0F,
            +0.5F, -0.5F,   0.0F, 1.0F, 0.0F,
            +0.5F, +0.5F,   0.0F, 0.0F, 1.0F,
            -0.5F, +0.5F,   1.0F, 1.0F, 0.0F
        };

        private static Buffer* indexBuffer;

        private static ushort[] indexBufferData =
        {
            0, 1, 2, // Triangle #0
            0, 2, 3 // Triangle #1
        };
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


            // Create instance
            InstanceDescriptor instanceDescriptor = new InstanceDescriptor();
            instance = wgpu.CreateInstance(in instanceDescriptor);
            if (instance == null)
            {
                throw new Exception("Could not initialize WebGPU!");
            }

            // Create surface
            surface = window.CreateWebGPUSurface(wgpu, instance);

            // Request Adapter
            RequestAdapterOptions requestAdapterOptions = new RequestAdapterOptions();
            requestAdapterOptions.CompatibleSurface = surface;
            requestAdapterOptions.PowerPreference = PowerPreference.HighPerformance;
            //requestAdapterOptions.BackendType = BackendType.Vulkan;
            requestAdapterOptions.ForceFallbackAdapter = false;
            wgpu.InstanceRequestAdapter(instance, in requestAdapterOptions, new PfnRequestAdapterCallback(
                (options, adapter1, message, _) =>
                {
                    if (options == RequestAdapterStatus.Success)
                    {
                        adapter = adapter1;
                    }
                    else
                    {
                        throw new Exception(
                            $"Could not get WebGPU adapter: {Marshal.PtrToStringAnsi((IntPtr)message)}");
                    }
                }), null);
            InspectingTheAdapter();

            // Request device
            DeviceDescriptor deviceDescriptor = new DeviceDescriptor();
            wgpu.AdapterRequestDevice(adapter, deviceDescriptor, new PfnRequestDeviceCallback(
                (options, device1, message, _) =>
                {
                    if (options == RequestDeviceStatus.Success)
                    {
                        device = device1;
                    }
                    else
                    {
                        throw new Exception($"Could not get WebGPU device:{Marshal.PtrToStringAnsi((IntPtr)message)}");
                    }
                }), null);

            wgpu.DeviceSetUncapturedErrorCallback(device,
                new PfnErrorCallback((errorType, message, @void) =>
                {
                    Console.WriteLine($"Uncaptured device error: type: {Marshal.PtrToStringAnsi((IntPtr)message)}");
                    throw new Exception(
                        $"Uncaptured device error: type: {Marshal.PtrToStringAnsi((IntPtr)message)}");
                }), null);

            // Get Queue
            queue = wgpu.DeviceGetQueue(device);

            // create vertexBuffer 
            vertexBuffer = wgpu.DeviceCreateBuffer(device,
                new BufferDescriptor() { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = (ulong)(sizeof(float) * vertexBufferData.Length) });
            fixed (void* ptr = vertexBufferData)
            {
                wgpu.QueueWriteBuffer(queue, vertexBuffer, 0, ptr, (UIntPtr)(sizeof(float) * vertexBufferData.Length));
            }

            // create indexBuffer 
            indexBuffer = wgpu.DeviceCreateBuffer(device,
                new BufferDescriptor() { Usage = BufferUsage.Index | BufferUsage.CopyDst, Size = (ulong)(sizeof(ushort) * indexBufferData.Length) });
            fixed (void* ptr = indexBufferData)
            {
                wgpu.QueueWriteBuffer(queue, indexBuffer, 0, ptr, (UIntPtr)(sizeof(ushort) * indexBufferData.Length));
            }
            // create pipeline

            CreatePipeline();

            // config surface
            CreateSwapChain();
            var swapchainFormat = wgpu.SurfaceGetPreferredFormat(surface, adapter);
        }

        private static void CreatePipeline()
        {
            // load shaderModule
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Code = (byte*)SilkMarshal.StringToPtr(shaderSource),
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    }
                };

                var shaderModuleDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = (ChainedStruct*)(&wgslDescriptor),
                };

                shaderModule = wgpu.DeviceCreateShaderModule(device, in shaderModuleDescriptor);
            }
            BlendState blendState = new BlendState()
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.SrcAlpha,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.Zero,
                    DstFactor = BlendFactor.One,
                    Operation = BlendOperation.Add
                }
            };
            ColorTargetState colorTargetState = new ColorTargetState()
            {
                Blend = &blendState,
                Format = wgpu.SurfaceGetPreferredFormat(surface, adapter),
                WriteMask = ColorWriteMask.All
            };

            FragmentState fragmentState = new FragmentState()
            {
                ConstantCount = 0,
                Constants = null,
                EntryPoint = (byte*)SilkMarshal.StringToPtr("fs_main"),
                Module = shaderModule,
                TargetCount = 1,
                Targets = &colorTargetState,
            };
            // create input layout

            var attributes = stackalloc[]
            {
                new VertexAttribute(){ Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 },
                new VertexAttribute(){ Format = VertexFormat.Float32x3, Offset = sizeof(float) * 2, ShaderLocation = 1 },
            };
            var vertexBufferLayout = new VertexBufferLayout()
            {
                ArrayStride = 5 * sizeof(float),
                AttributeCount = 2,
                Attributes = attributes,
                StepMode = VertexStepMode.Vertex
            };
            RenderPipelineDescriptor renderPipelineDescriptor = new RenderPipelineDescriptor
            {
                DepthStencil = null,
                Vertex = new VertexState()
                {
                    BufferCount = 1,
                    Buffers = &vertexBufferLayout,
                    ConstantCount = 0,
                    Constants = null,
                    EntryPoint = (byte*)SilkMarshal.StringToPtr("vs_main"),
                    Module = shaderModule,
                },
                Primitive = new PrimitiveState()
                {
                    CullMode = CullMode.None,
                    FrontFace = FrontFace.Ccw,
                    StripIndexFormat = IndexFormat.Undefined,
                    Topology = PrimitiveTopology.TriangleList
                },
                Fragment = &fragmentState,
                Multisample = new MultisampleState()
                {
                    Count = 1,
                    Mask = ~0u,
                    AlphaToCoverageEnabled = false,
                },
                Label = null,
                Layout = null,
            };
            renderPipeline = wgpu.DeviceCreateRenderPipeline(device, renderPipelineDescriptor);
        }
        private static void CreateSwapChain()
        {
            SurfaceConfiguration configuration = new SurfaceConfiguration
            {
                Device = device,
                Format = wgpu.SurfaceGetPreferredFormat(surface, adapter),
                Usage = TextureUsage.RenderAttachment,
                Width = (uint)window.FramebufferSize.X,
                Height = (uint)window.FramebufferSize.Y,
                PresentMode = PresentMode.Fifo,
                AlphaMode = CompositeAlphaMode.Opaque
            };

            wgpu.SurfaceConfigure(surface, in configuration);
        }

        private static void WindowOnRender(double obj)
        {
            // Create CommandEncoder
            CommandEncoderDescriptor commandEncoderDescriptor = new CommandEncoderDescriptor();
            var encoder = wgpu.DeviceCreateCommandEncoder(device, in commandEncoderDescriptor);

            // Get texture
            SurfaceTexture surfaceTexture;
            wgpu.SurfaceGetCurrentTexture(surface, &surfaceTexture);
            switch (surfaceTexture.Status)
            {
                case SurfaceGetCurrentTextureStatus.Timeout:
                case SurfaceGetCurrentTextureStatus.Outdated:
                case SurfaceGetCurrentTextureStatus.Lost:
                    // Recreate swapchain,
                    wgpu.TextureRelease(surfaceTexture.Texture);
                    CreateSwapChain();
                    // Skip this frame
                    return;
                case SurfaceGetCurrentTextureStatus.OutOfMemory:
                case SurfaceGetCurrentTextureStatus.DeviceLost:
                case SurfaceGetCurrentTextureStatus.Force32:
                    throw new Exception($"What is going on bros... {surfaceTexture.Status}");
            }

            TextureView* view = wgpu.TextureCreateView(surfaceTexture.Texture, null);

            RenderPassColorAttachment renderPassColorAttachment = new RenderPassColorAttachment
            {
                LoadOp = LoadOp.Clear,
                ClearValue = new Color(0.9, 0.1, 0.2, 1.0),
                StoreOp = StoreOp.Store,
                View = view
            };
            RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &renderPassColorAttachment,
                DepthStencilAttachment = null,
                TimestampWrites = null,
                OcclusionQuerySet = null
            };
            RenderPassEncoder* renderPassEncoder = wgpu.CommandEncoderBeginRenderPass(encoder, in renderPassDescriptor);
            wgpu.RenderPassEncoderSetPipeline(renderPassEncoder, renderPipeline);
            wgpu.RenderPassEncoderSetVertexBuffer(renderPassEncoder, 0, vertexBuffer, 0, (ulong)(sizeof(float) * vertexBufferData.Length));
            wgpu.RenderPassEncoderSetIndexBuffer(renderPassEncoder, indexBuffer, IndexFormat.Uint16, 0, (ulong)(sizeof(ushort) * indexBufferData.Length));

            wgpu.RenderPassEncoderDrawIndexed(renderPassEncoder, (uint)indexBufferData.Length, 1, 0, 0, 0);
            wgpu.RenderPassEncoderEnd(renderPassEncoder);
            // encoder you command
            CommandBufferDescriptor commandBufferDescriptor = new CommandBufferDescriptor();
            CommandBuffer* commandBuffer = wgpu.CommandEncoderFinish(encoder, commandBufferDescriptor);

            // submit queue
            wgpu.QueueSubmit(queue, 1, &commandBuffer);
            //wgpu.QueueOnSubmittedWorkDone(queue, new PfnQueueWorkDoneCallback((status, _) =>
            //{
            //    Console.WriteLine($"Queued work finished with status: {status}");
            //}), null);
            wgpu.SurfacePresent(surface);

            // release
            wgpu.CommandBufferRelease(commandBuffer);
            wgpu.RenderPassEncoderRelease(renderPassEncoder);
            wgpu.CommandEncoderRelease(encoder);
            wgpu.TextureRelease(surfaceTexture.Texture);
            wgpu.TextureViewRelease(view);

        }

        private static void WindowOnUpdate(double obj)
        {
        }

        private static void WindowOnFramebufferResize(Vector2D<int> obj)
        {
            CreateSwapChain();
        }

        private static void WindowOnClosing()
        {
            wgpu.BufferDestroy(vertexBuffer);
            wgpu.BufferRelease(vertexBuffer);
            wgpu.RenderPipelineRelease(renderPipeline);
            wgpu.QueueRelease(queue);
            wgpu.DeviceRelease(device);
            wgpu.AdapterRelease(adapter);
            wgpu.SurfaceRelease(surface);
            wgpu.InstanceRelease(instance);
        }
        private static void InspectingTheAdapter()
        {
            // features
            {
                var count = (int)wgpu.AdapterEnumerateFeatures(adapter, null);
                var features = stackalloc FeatureName[count];
                wgpu.AdapterEnumerateFeatures(adapter, features);
                for (int i = 0; i < count; i++)
                {
                    Console.WriteLine(features[i]);
                }
            }
            // properties
            {
                AdapterProperties properties = new AdapterProperties();
                wgpu.AdapterGetProperties(adapter, &properties);
                Console.WriteLine(properties.BackendType);
                Console.WriteLine(properties.AdapterType);
                Console.WriteLine(properties.DeviceID);
                Console.WriteLine(properties.VendorID);
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.Name));
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.Architecture));
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.VendorName));
                Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)properties.DriverDescription));
            }

            // limits
            {
                SupportedLimits limits = new SupportedLimits();
                wgpu.AdapterGetLimits(adapter, &limits);
            }
        }
    }
}
