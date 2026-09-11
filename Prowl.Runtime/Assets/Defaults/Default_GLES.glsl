#version 300 es
precision mediump float;
precision highp int;

in vec3 vPosition;
in vec2 vTexCoord;

out vec4 FragColor;
uniform sampler2D uMainTexture;

void main()
{
    FragColor = texture(uMainTexture, vTexCoord);
}
