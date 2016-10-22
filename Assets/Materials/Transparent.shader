Shader "Transparent" {
	Properties{
		_MainTex("Base (RGB) Trans (A)", 2D) = "white" { }
		_RenderIndex("Render Index (Z-Order)", int) = 1
	}

	SubShader{
		Tags{ "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "Preview Type" = "Plane" }

		Cull Back
		LOD 200 // level of detail = diffuse
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
