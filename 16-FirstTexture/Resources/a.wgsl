@group(0) @binding(0) var gradientTexture: texture_2d<f32>;
@group(0) @binding(1) var textureSampler: sampler;

struct VertexInput {
    @location(0) position: vec2f,
    @location(1) texCoords: vec2f,
};

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) texCoords: vec2f,
};

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.position = vec4f(in.position, 0.0, 1.0);
    out.texCoords = in.texCoords;
    return out;
}


@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4f {
    return vec4f(textureSample(gradientTexture,textureSampler,in.texCoords).rgb,1.0);
}