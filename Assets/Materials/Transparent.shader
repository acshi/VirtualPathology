Shader "Transparent" {
	Properties{
		_MainTex("Base (RGB) Trans (A)", 2D) = "white" { }
		_UseTransferFunction("Use H&E Stain Transfer Function", Int) = 1
		_TransparencyScalar("Transparency Scalar", Float) = 0.5
		_Contrast("Contrast", Float) = 1.0
	}

	SubShader{
		Tags{ "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

		Cull Back
		LOD 200 // level of detail = diffuse
		
		// Write depth only first (https://community.unity.com/t5/Shaders/Transparent-Depth-Shader-good-for-ghosts/td-p/1108978)
		Pass {
			ZWrite On
			ColorMask 0

			CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#include "UnityCG.cginc"

				struct v2f {
					float4 pos : SV_POSITION;
				};

				v2f vert(appdata_base v) {
					v2f o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
					return o;
				}

				half4 frag(v2f i) : COLOR {
					return half4 (0, 0, 0, 0);
				}
			ENDCG
		}

		CGPROGRAM
			#pragma surface surf Lambert alpha
			sampler2D _MainTex;
			int _UseTransferFunction;
			float _TransparencyScalar;
			float _Contrast;

			struct Input {
				float2 uv_MainTex;
			};

			void surf(Input IN, inout SurfaceOutput o) {
				float4 c = tex2D(_MainTex, IN.uv_MainTex);
				c.r *= _Contrast;
				c.g *= _Contrast;
				c.b *= _Contrast;

				// Some normalization for transparency because the other factors affect it too
				float transparency = _TransparencyScalar * (_UseTransferFunction ? 2 : 1);
				transparency = transparency + transparency * (_Contrast - 1.0) * 0.25;

				if (_UseTransferFunction) {
					o.Albedo.r = pow(10, -c.g * 0.046 - c.r * 0.490);
					o.Albedo.g = pow(10, -c.g * 0.842 - c.r * 0.769);
					o.Albedo.b = pow(10, -c.g * 0.537 - c.r * 0.410);
				} else {
					o.Albedo = c.rgb;
				}
				if (transparency == 0.0) {
					o.Alpha = 1.0;
				} else {
					float baseA = o.Albedo.r * 0.299 + o.Albedo.g * 0.587 + o.Albedo.b * 0.114;
					if (_UseTransferFunction) {
						baseA = 1.0 - baseA;
					}
					o.Alpha = baseA / transparency;
				}
			}
		ENDCG
	}

	FallBack "Mobile/Diffuse"
}
