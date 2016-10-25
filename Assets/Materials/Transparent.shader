Shader "Transparent" {
	Properties{
		_MainTex("Base (RGB) Trans (A)", 2D) = "white" { }
	}

	SubShader{
		Tags{ "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

		Cull Off
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

			struct Input {
				float2 uv_MainTex;
			};

			void surf(Input IN, inout SurfaceOutput o) {
				float transparency = 1.0;

				float4 c = tex2D(_MainTex, IN.uv_MainTex);
				o.Albedo = c.rgb;
				if (transparency == 0.0) {
					o.Alpha = 1.0;
				} else {
					float baseA = c.r * 0.299 + c.g * 0.587 + c.b * 0.114;
					o.Alpha = baseA / transparency;
				}
			}
		ENDCG

		/*Cull Back
		LOD 200 // level of detail = diffuse
		CGPROGRAM
			#pragma surface surf Lambert alpha
			sampler2D _MainTex;
			float _Transparency;

			struct Input {
				float2 uv_MainTex;
			};

			void surf(Input IN, inout SurfaceOutput o) {
				float4 c = tex2D(_MainTex, IN.uv_MainTex);
				o.Albedo = c.rgb;
				float baseA = c.r * 0.299 + c.g * 0.587 + c.b * 0.114;
				o.Alpha = baseA / _Transparency;
			}
		ENDCG*/
		
		// Set up alpha blending
		//Blend SrcAlpha OneMinusSrcAlpha

		// Render the back facing parts of the object.
		// If the object is convex, these will always be further away
		// than the front-faces.
		/*Pass{
			Cull Front
			SetTexture[_MainTex]{
				Combine Texture
			}
		}
			// Render the parts of the object facing us.
			// If the object is convex, these will be closer than the
			// back-faces.
		Pass{
			Cull Back
			SetTexture[_MainTex]{
				Combine Texture
			}
		}*/
	}
}
