// A delver is drawn from a grid of indices and a palette of ninety-odd colours, not from a
// picture. Two of the role keys are not colours at all — a shadow pixel means "whatever is
// beneath this, one tone darker" — so what a pixel finally IS depends on everything stamped
// before it, and there is no sprite that could have been baked in advance.
//
// That leaves the colour lookup to happen somewhere, and here is the cheap place for it: the
// grid never changes while a delver wears the same clothes, so dragging a swatch in the
// Changing Room rewrites a texture one pixel tall and every delver on screen recolours at once.
//
// Otherwise this is UI/Default. The stencil block, the clip rect and the alpha clip are all
// there because a Canvas needs them — a mask stops working the moment a UI shader forgets them,
// and it stops working silently.
Shader "RelicRun/Hero Palette Swap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Index Grid", 2D) = "black" {}
        _Palette ("Palette", 2D) = "white" {}

        // How many colours this delver has. The lookup samples the middle of a texel, so it
        // needs the width; sampling by a normalised index alone would land between two.
        _PaletteSize ("Palette Entries", Float) = 256

        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _Palette;
            float _PaletteSize;
            fixed4 _Color;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // The grid is a linear single-channel texture, so this byte comes back as
                // exactly n/255 and rounds to the index that was written. Anything that made
                // the texture sRGB would put every index but zero somewhere else entirely.
                float index = floor(tex2D(_MainTex, IN.texcoord).r * 255.0 + 0.5);

                float u = (index + 0.5) / max(_PaletteSize, 1.0);
                half4 colour = tex2D(_Palette, float2(u, 0.5)) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                colour.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(colour.a - 0.001);
                #endif

                return colour;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
