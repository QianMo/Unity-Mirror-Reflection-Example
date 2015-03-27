//实现镜面反射的Shader(mirror reflection Shader)
Shader "Mirror Reflection" 
{ 
	//一些主属性（Some Properties）
	Properties 
	{
		_MainTex ("Base (RGB)", 2D) = "white" { }
		_ReflectionTex ("Reflection", 2D) = "white" { TexGen ObjectLinear }
		_ReflectionColor("ReflectionColor",Color) = (0.3,0.3,0.3,0.3)
	}

	// 子着色器1:3个SetTexture搞定镜面效果（Subshader1:three SetTexture And Get it）
	Subshader 
	{ 
		Pass 
		{
			SetTexture[_MainTex] { combine texture }

			SetTexture[_ReflectionTex] 
			{ 
				matrix [_ProjMatrix] 
				combine texture * previous 
			}

			SetTexture[_MainTex]
			{
				ConstantColor[_ReflectionColor]
				combine constant * previous DOUBLE
			}
		}
	}

	//子着色器2：fallback，就是一个备胎，渲染主纹理（Subshader2: fallback(just main texture)）
	Subshader 
	{
		Pass 
		{
			SetTexture [_MainTex] { combine texture }
		}
	}

}
