// Unbeleuchteter Shader: Farbe = Punktfarbe des Modells × Farbe.
// Für den Mars: Licht und Schatten sind schon in die Punktfarben eingerechnet,
// damit der Satellit auf seiner ganzen Runde eine beleuchtete Oberfläche sieht.
Shader "Ocean/Vertexfarbe"
{
    Properties
    {
        _BaseColor ("Farbe", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Eingang { float4 position : POSITION; half4 farbe : COLOR; };
            struct Zwischen { float4 positionCS : SV_POSITION; half4 farbe : COLOR; };

            Zwischen Vert(Eingang e)
            {
                Zwischen z;
                z.positionCS = TransformObjectToHClip(e.position.xyz);
                z.farbe = e.farbe;
                return z;
            }

            half4 Frag(Zwischen z) : SV_Target
            {
                return half4(z.farbe.rgb * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
