// Boden-Shader für Insel und Meeresgrund.
// Wie Unitys Standard-Material, aber die Farbe wird zusätzlich mit der Punktfarbe des Modells
// multipliziert. So kann der Boden mit zunehmender Wassertiefe dunkler werden:
// flaches Wasser wirkt hell-türkis, tiefes Wasser dunkelblau.
Shader "Ocean/Boden"
{
    Properties
    {
        _BaseMap ("Textur", 2D) = "white" {}
        _BaseColor ("Farbe", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Eingang
            {
                float4 position : POSITION;
                float3 normal   : NORMAL;
                float2 uv       : TEXCOORD0;
                half4  farbe    : COLOR;
            };

            struct Zwischen
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half   nebel      : TEXCOORD3;
                half4  farbe      : COLOR;
            };

            Zwischen Vert(Eingang e)
            {
                Zwischen z;
                VertexPositionInputs p = GetVertexPositionInputs(e.position.xyz);
                z.positionCS = p.positionCS;
                z.positionWS = p.positionWS;
                z.normalWS = TransformObjectToWorldNormal(e.normal);
                z.uv = TRANSFORM_TEX(e.uv, _BaseMap);
                z.farbe = e.farbe;
                z.nebel = ComputeFogFactor(p.positionCS.z);
                return z;
            }

            half4 Frag(Zwischen z) : SV_Target
            {
                half3 grundfarbe = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, z.uv).rgb * _BaseColor.rgb * z.farbe.rgb;
                half3 normale = normalize(z.normalWS);

                Light sonne = GetMainLight(TransformWorldToShadowCoord(z.positionWS));
                half3 direkt = sonne.color * saturate(dot(normale, sonne.direction)) * sonne.shadowAttenuation;
                half3 umgebung = SampleSH(normale);

                half3 ergebnis = grundfarbe * (direkt + umgebung);
                ergebnis = MixFog(ergebnis, z.nebel);
                return half4(ergebnis, 1);
            }
            ENDHLSL
        }

        // Nur Tiefe (wird von URP für manche Effekte gebraucht)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex VertTiefe
            #pragma fragment FragTiefe
            float4 VertTiefe(float4 position : POSITION) : SV_POSITION { return TransformObjectToHClip(position.xyz); }
            half FragTiefe() : SV_Target { return 0; }
            ENDHLSL
        }

        // Tiefe + Normalen (z. B. für Umgebungsverdeckung)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex VertNormalen
            #pragma fragment FragNormalen

            struct EingangN { float4 position : POSITION; float3 normal : NORMAL; };
            struct ZwischenN { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            ZwischenN VertNormalen(EingangN e)
            {
                ZwischenN z;
                z.positionCS = TransformObjectToHClip(e.position.xyz);
                z.normalWS = TransformObjectToWorldNormal(e.normal);
                return z;
            }
            half4 FragNormalen(ZwischenN z) : SV_Target { return half4(normalize(z.normalWS), 0); }
            ENDHLSL
        }
    }
}
