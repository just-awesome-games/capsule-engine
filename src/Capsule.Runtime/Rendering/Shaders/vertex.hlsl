// Capsule's sprite vertex stage, which every sprite shader shares. Sprites are pulled, not pushed. The
// renderer uploads one CapsuleSprite per sprite into a storage buffer, binds no vertex buffer and draws
// six vertices a sprite. Each vertex reads its sprite at SV_VertexID / 6 and places its own corner. This
// is the one place a sprite is placed. A polygon's triangle rides the same buffer as a sprite flagged
// CapsuleTriangle, whose fields hold its three corners instead.
//
// CapsuleSprite and CapsuleVertexUniforms are the runtime's SpriteInstance and SpriteUniforms, member for
// member, in SDL's binding layout: the sprites in storage buffer slot 0, the uniforms in uniform buffer 0.
// The stage snaps in float, which every GPU format has. Where CapsuleGrid is set the frame snaps, as the
// runtime's GridPlacement describes: each end of the step is snapped from its own layer corner at its own grid,
// and the two ends' whole pixels are carried between by the frame's fraction. Otherwise a point is interpolated
// and placed from CapsuleLayerCorner. Its outputs are the pixel stage's inputs in
// location order: tint, flash, texture coordinate with where the pixel falls on the light after it, coverage,
// quad, offset from the pivot. The tint, the flash and the coverage are the same at every corner and are not
// interpolated, which a GPU would pay for on every pixel it shades. The build compiles this file to the
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
    float Intensity;
};

StructuredBuffer<CapsuleSprite> CapsuleSprites : register(t0, space0);

cbuffer CapsuleVertexUniforms : register(b0, space1)
{
    row_major float4x4 MatrixTransform;
    float2 CapsuleTexelSize;
    float2 CapsulePreviousLayerCorner;
    float2 CapsuleLayerCorner;
    float2 CapsuleFrameCorner;
    float2 CapsuleShift;
    float2 CapsuleGrids;
    float CapsuleAlpha;
    float CapsuleGrid;
    float CapsuleCoverage;
    uint CapsuleFirstSprite;
    float4 CapsuleLightPlace;
};

struct CapsuleVertexOutput
{
    [[vk::location(0)]] nointerpolation float4 Tint : COLOR0;
    [[vk::location(1)]] nointerpolation float4 Flash : COLOR1;
    [[vk::location(2)]] float4 UV : TEXCOORD0;
    [[vk::location(3)]] nointerpolation float Coverage : TEXCOORD1;
    [[vk::location(4)]] float4 Quad : TEXCOORD2;
    [[vk::location(5)]] float2 FromPivot : TEXCOORD3;
    float4 Position : SV_Position;
};

static const uint CapsuleFlipX = 1;
static const uint CapsuleFlipY = 2;
static const uint CapsuleWhole = 4;
static const uint CapsuleTriangle = 8;
static const float CapsulePi = 3.14159265358979;
static const float CapsuleTau = 6.28318530717959;

float4 CapsuleUnpack(uint color)
{
    return float4(color & 0xFF, (color >> 8) & 0xFF, (color >> 16) & 0xFF, color >> 24) / 255.0;
}

// The tint's colour scaled by the sprite's intensity, which is 1 on every quad but a light's.
float4 CapsuleTint(CapsuleSprite sprite)
{
    float4 tint = CapsuleUnpack(sprite.Tint);
    return float4(tint.rgb * sprite.Intensity, tint.a);
}

// Where a clip-space position falls on the light, which covers the target the viewport is placed in. The pixel
// stage reads the light there, interpolated beside the texture coordinate, which costs a GPU less per pixel than
// reading it at the pixel's own position.
float2 CapsuleLightUV(float4 position)
{
    return (position.xy * CapsuleLightPlace.xy) + CapsuleLightPlace.zw;
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

// Whole pixels at the frame's fraction of the way from one end of the step to the other. The travel alone is
// rounded, by CapsulePixel's midpoint rule. Points whose ends lie the same pixels apart change pixel on the same
// frame, and a point whose ends agree holds them.
float2 CapsuleBetweenPixels(float2 previous, float2 current)
{
    precise float2 offset = ((current - previous) * CapsuleAlpha) + 0.5;
    return previous + floor(offset - (1.0 / 32.0));
}

// The whole pixels from the frame's corner a point given at both ends of the step is drawn at.
float2 CapsuleSnapped(float2 previous, float2 current)
{
    float2 from = CapsulePixel(CapsulePreviousLayerCorner, previous, CapsuleGrids.x);
    float2 to = CapsulePixel(CapsuleLayerCorner, current, CapsuleGrids.y) + CapsuleShift;
    return CapsuleBetweenPixels(from, to);
}

// One end's edges of a quad at a whole number of quarter turns, in whole pixels from the frame's corner: the low
// corner, then the high. near and far are the corners' offsets from the pivot, and sized the drawn size on the
// surface's axes. A size of whole pixels is drawn that size. Its far edge is a sum a float can hold less finely
// than the near edge, and the sum's own pixel can fall one off. Any other size ends on its far edge's pixel,
// where the next quad begins.
float4 CapsuleEdges(float2 layerCorner, float grid, float2 position, float2 near, float2 far, float2 sized, bool unturned)
{
    precise float2 low = position + near;
    precise float2 high = unturned ? low + sized : position + far;
    float2 left = CapsulePixel(layerCorner, low, grid);
    float2 right = CapsulePixel(layerCorner, high, grid);
    float2 span = sized * grid;
    return float4(left, float2(frac(span.x) == 0.0 ? left.x + span.x : right.x, frac(span.y) == 0.0 ? left.y + span.y : right.y));
}

// A point at the frame's fraction of the way across the step, exactly either end where it did not move.
float2 CapsuleBetween(float2 previous, float2 current)
{
    precise float2 between = previous + ((current - previous) * CapsuleAlpha);
    return all(previous == current) ? current : between;
}

// One vertex of a polygon's triangle, numbered within its six. The corners are interpolated, then the
// triangle moves by however far its first corner moves to land where the grid, where it is set, draws it.
// Every triangle of a polygon starts at the polygon's first point and moves alike. The polygon keeps its
// shape as it travels, and its triangles meet without a seam. The quad and the offset from the pivot both
// carry the corner's offset from that first point, and the quad no extent. Vertices three to five collapse
// onto the first corner and draw nothing.
CapsuleVertexOutput CapsuleTriangleVertex(CapsuleSprite sprite, uint index)
{
    precise float2 first = CapsuleBetween(sprite.PreviousPosition, sprite.Position);
    precise float2 corner = index == 1 ? CapsuleBetween(float2(sprite.PreviousRotation, sprite.Rotation), sprite.Origin)
        : index == 2 ? CapsuleBetween(sprite.Size, float2(asfloat(sprite.Region), asfloat(sprite.Extent)))
        : first;
    precise float2 placed = CapsuleGrid > 0.0
        ? CapsuleFrameCorner + (CapsuleSnapped(sprite.PreviousPosition, sprite.Position) / CapsuleGrid)
        : CapsuleFrameCorner + (first - CapsuleLayerCorner);
    precise float2 offset = corner - first;

    CapsuleVertexOutput output;
    output.Position = mul(float4(placed + offset, 0.0, 1.0), MatrixTransform);
    output.Tint = CapsuleTint(sprite);
    output.Flash = CapsuleUnpack(sprite.Flash);
    output.UV = float4(0.5, 0.5, CapsuleLightUV(output.Position));
    output.Coverage = CapsuleCoverage;
    output.Quad = float4(offset, 0.0, 0.0);
    output.FromPivot = offset;
    return output;
}

// One sprite's six vertices, numbered from SV_VertexID: the sprite interpolated by the frame's
// fraction, moved from its layer's corner onto the frame's, and placed, or where the grid is set,
// snapped at both ends of the step and carried between them. A quad snapped at a whole number of
// quarter turns has every edge on the grid.
// A whole texture stands where it is. The diagonal runs top right to bottom left.
CapsuleVertexOutput CapsuleVertex(uint vertexId : SV_VertexID)
{
    CapsuleSprite sprite = CapsuleSprites[CapsuleFirstSprite + (vertexId / 6)];
    if ((sprite.Flags & CapsuleTriangle) != 0)
    {
        return CapsuleTriangleVertex(sprite, vertexId % 6);
    }

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
            // Every edge on the grid, at the exact quarter turn the frame draws, at both ends of the step. The
            // edges are carried between the ends, not the size, which changes with the grid.
            int turns = rotation == 0.0 ? 0 : ((int(nearest) % 4) + 4) % 4;
            precise float2 a = -(origin * scale);
            precise float2 b = sprite.Size - (origin * scale);
            precise float2 near = turns == 0 ? a : turns == 1 ? float2(-b.y, a.x) : turns == 2 ? -b : float2(a.y, -b.x);
            precise float2 far = turns == 0 ? b : turns == 1 ? float2(-a.y, b.x) : turns == 2 ? -a : float2(b.y, -a.x);
            float2 sized = (turns & 1) == 0 ? sprite.Size : sprite.Size.yx;
            float4 from = CapsuleEdges(CapsulePreviousLayerCorner, CapsuleGrids.x, sprite.PreviousPosition, near, far, sized, rotation == 0.0);
            float4 to = CapsuleEdges(CapsuleLayerCorner, CapsuleGrids.y, sprite.Position, near, far, sized, rotation == 0.0) + CapsuleShift.xyxy;
            float2 left = CapsuleBetweenPixels(from.xy, to.xy);
            float2 extent = CapsuleBetweenPixels(from.zw, to.zw) - left;
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
                ? CapsuleFrameCorner + (CapsuleSnapped(sprite.PreviousPosition, sprite.Position) / CapsuleGrid)
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
    output.Tint = CapsuleTint(sprite);
    output.Flash = CapsuleUnpack(sprite.Flash);
    output.UV = float4(corner & 1 ? uvTo.x : uvFrom.x, corner >> 1 ? uvTo.y : uvFrom.y, CapsuleLightUV(output.Position));
    output.Coverage = CapsuleCoverage;
    output.Quad = float4(abs(drawn) * axes, abs(drawn));

    // From sprite.Origin at the final scale, never the local origin, which the grid's branch zeroes after
    // folding it into the position. Origin is mirrored on a flipped axis and the corners are not, so the
    // offset stays unmirrored whichever way the frame faces.
    output.FromPivot = (abs(drawn) * axes) - (sprite.Origin * abs(scale));
    return output;
}
