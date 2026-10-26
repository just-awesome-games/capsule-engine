// Capsule's sprite vertex stage, which every sprite shader shares. Sprites are pulled, not pushed. The
// renderer uploads one CapsuleSprite per sprite into a storage buffer, binds no vertex buffer and draws
// six vertices a sprite. Each vertex reads its sprite at SV_VertexID / 6 and places its own corner. This
// is the one place a sprite is placed.
//
// CapsuleSprite and CapsuleVertexUniforms are the runtime's SpriteInstance and SpriteUniforms, member for
// member, in SDL's binding layout: the sprites in storage buffer slot 0, the uniforms in uniform buffer 0.
// The stage snaps in float, which every GPU format has. Its outputs are the pixel stage's inputs in
// location order: tint, flash, texture coordinate, coverage. The build compiles this file to the
// vertex.<format>.shader files beside it.

struct CapsuleSprite
{
    float2 PreviousPosition;
    float2 Position;
    float PreviousRotation;
    float Rotation;
    float2 Origin;
    float2 Size;
    uint Region;
    uint Extent;
    uint Tint;
    uint Flash;
    uint Flags;
    uint Padding;
};

StructuredBuffer<CapsuleSprite> CapsuleSprites : register(t0, space0);

cbuffer CapsuleVertexUniforms : register(b0, space1)
{
    row_major float4x4 MatrixTransform;
    float2 CapsuleTexelSize;
    float2 CapsuleLayerCorner;
    float2 CapsuleFrameCorner;
    float CapsuleAlpha;
    float CapsuleGrid;
    float CapsuleCoverage;
    uint CapsuleFirstSprite;
};

struct CapsuleVertexOutput
{
    [[vk::location(0)]] float4 Tint : COLOR0;
    [[vk::location(1)]] float4 Flash : COLOR1;
    [[vk::location(2)]] float2 UV : TEXCOORD0;
    [[vk::location(3)]] float Coverage : TEXCOORD1;
    float4 Position : SV_Position;
};

static const uint CapsuleFlipX = 1;
static const uint CapsuleFlipY = 2;
static const uint CapsuleWhole = 4;
static const float CapsulePi = 3.14159265358979;
static const float CapsuleTau = 6.28318530717959;

float4 CapsuleUnpack(uint color)
{
    return float4(color & 0xFF, (color >> 8) & 0xFF, (color >> 16) & 0xFF, color >> 24) / 255.0;
}

// The whole surface pixel value lands on, counted from origin. A midpoint rounds down by a margin
// wider than float noise.
float CapsulePixel(float origin, float value, float scale)
{
    precise float offset = ((value - origin) * scale) + 0.5;
    return floor(offset - (1.0 / 32.0));
}

float2 CapsulePixel(float2 origin, float2 value, float scale)
{
    return float2(CapsulePixel(origin.x, value.x, scale), CapsulePixel(origin.y, value.y, scale));
}

// One sprite's six vertices, numbered from SV_VertexID: the sprite interpolated by the frame's
// fraction, moved from its layer's corner onto the frame's, snapped to whole pixels where the grid
// is set, and placed. A quad snapped at a whole number of quarter turns has every edge on the grid.
// A whole texture stands where it is. The diagonal runs top right to bottom left.
CapsuleVertexOutput CapsuleVertex(uint vertexId : SV_VertexID)
{
    CapsuleSprite sprite = CapsuleSprites[CapsuleFirstSprite + (vertexId / 6)];

    // Top left, top right, bottom left, then top right, bottom right, bottom left.
    uint corner = (0xB64u >> ((vertexId % 6) * 2)) & 3;
    float2 axes = float2(corner & 1, corner >> 1);

    float2 texels = float2(sprite.Extent & 0xFFFF, sprite.Extent >> 16);
    float2 region = float2(sprite.Region & 0xFFFF, sprite.Region >> 16);
    bool whole = (sprite.Flags & CapsuleWhole) != 0;
    precise float2 texTopLeft = whole ? float2(0.0, 0.0) : region * CapsuleTexelSize;
    precise float2 texBottomRight = whole ? float2(1.0, 1.0) : (region + texels) * CapsuleTexelSize;

    precise float2 position = sprite.Position;
    precise float2 origin = sprite.Origin;
    precise float2 scale = sprite.Size;
    precise float sine = 0.0;
    precise float cosine = 1.0;
    bool turned = false;

    if (whole)
    {
        if (sprite.Rotation != 0.0)
        {
            sincos(sprite.Rotation, sine, cosine);
            turned = true;
        }
    }
    else
    {
        if (any(sprite.PreviousPosition != sprite.Position))
        {
            position = sprite.PreviousPosition + ((sprite.Position - sprite.PreviousPosition) * CapsuleAlpha);
        }

        precise float rotation = sprite.Rotation;
        if (sprite.PreviousRotation != sprite.Rotation)
        {
            precise float turn = sprite.Rotation - sprite.PreviousRotation;
            turn -= round(turn / CapsuleTau) * CapsuleTau;
            if (turn <= -CapsulePi)
            {
                turn += CapsuleTau;
            }

            rotation = sprite.PreviousRotation + (turn * CapsuleAlpha);
        }

        precise float quarters = rotation * (2.0 / CapsulePi);
        precise float nearest = round(quarters);
        scale = sprite.Size / texels;

        if (CapsuleGrid > 0.0 && (rotation == 0.0 || abs(quarters - nearest) <= 1e-4))
        {
            // Every edge on the grid, at the exact quarter turn.
            int turns = rotation == 0.0 ? 0 : ((int(nearest) % 4) + 4) % 4;
            precise float2 a = -(origin * scale);
            precise float2 b = sprite.Size - (origin * scale);
            precise float2 low = position + (turns == 0 ? a : turns == 1 ? float2(-b.y, a.x) : turns == 2 ? -b : float2(a.y, -b.x));
            precise float2 high = rotation == 0.0 ? low + sprite.Size
                : position + (turns == 0 ? b : turns == 1 ? float2(-a.y, b.x) : turns == 2 ? -a : float2(b.y, -a.x));
            float2 left = CapsulePixel(CapsuleLayerCorner, low, CapsuleGrid);
            float2 extent = CapsulePixel(CapsuleLayerCorner, high, CapsuleGrid) - left;
            precise float2 snappedLow = CapsuleFrameCorner + (left / CapsuleGrid);
            precise float2 snappedHigh = snappedLow + (extent / CapsuleGrid);
            float2 pixels = (turns & 1) == 0 ? extent : extent.yx;
            position = turns == 0 ? snappedLow
                : turns == 1 ? float2(snappedHigh.x, snappedLow.y)
                : turns == 2 ? snappedHigh
                : float2(snappedLow.x, snappedHigh.y);
            scale = pixels / (texels * CapsuleGrid);
            origin = float2(0.0, 0.0);
            turned = turns != 0;
            sine = turns == 1 ? 1.0 : turns == 3 ? -1.0 : 0.0;
            cosine = turns == 0 ? 1.0 : turns == 2 ? -1.0 : 0.0;
        }
        else
        {
            position = CapsuleGrid > 0.0
                ? CapsuleFrameCorner + (CapsulePixel(CapsuleLayerCorner, position, CapsuleGrid) / CapsuleGrid)
                : CapsuleFrameCorner + (position - CapsuleLayerCorner);
            if (rotation != 0.0)
            {
                sincos(rotation, sine, cosine);
                turned = true;
            }
        }
    }

    // A flip swaps the texture coordinates on its axis and leaves the corners in place.
    float2 uvFrom = float2((sprite.Flags & CapsuleFlipX) != 0 ? texBottomRight.x : texTopLeft.x, (sprite.Flags & CapsuleFlipY) != 0 ? texBottomRight.y : texTopLeft.y);
    float2 uvTo = float2((sprite.Flags & CapsuleFlipX) != 0 ? texTopLeft.x : texBottomRight.x, (sprite.Flags & CapsuleFlipY) != 0 ? texTopLeft.y : texBottomRight.y);

    precise float2 drawnOrigin = origin * scale;
    precise float2 drawn = texels * scale;

    // A quad mirrored on one axis by a negative size has no area, and draws nothing.
    if ((drawn.x < 0.0) != (drawn.y < 0.0))
    {
        drawn = float2(0.0, 0.0);
    }
    precise float2 vertex;
    if (!turned)
    {
        vertex = (position - drawnOrigin) + (drawn * axes);
    }
    else
    {
        precise float2 d = (float2(0.0, 0.0) - drawnOrigin) + (drawn * axes);
        vertex = float2(position.x + (d.x * cosine) - (d.y * sine), position.y + (d.x * sine) + (d.y * cosine));
    }

    CapsuleVertexOutput output;
    output.Position = mul(float4(vertex, 0.0, 1.0), MatrixTransform);
    output.Tint = CapsuleUnpack(sprite.Tint);
    output.Flash = CapsuleUnpack(sprite.Flash);
    output.UV = float2(corner & 1 ? uvTo.x : uvFrom.x, corner >> 1 ? uvTo.y : uvFrom.y);
    output.Coverage = CapsuleCoverage;
    return output;
}
