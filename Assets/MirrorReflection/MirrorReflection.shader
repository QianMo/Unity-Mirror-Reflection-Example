//ʵ�־��淴���Shader(mirror reflection Shader)
Shader "Mirror Reflection" 
{ 
	//һЩ�����ԣ�Some Properties��
	Properties 
	{
		_MainTex ("Base (RGB)", 2D) = "white" { }
		_ReflectionTex ("Reflection", 2D) = "white" { TexGen ObjectLinear }
		_ReflectionColor("ReflectionColor",Color) = (0.3,0.3,0.3,0.3)
	}

	// ����ɫ��1:3��SetTexture�㶨����Ч����Subshader1:three SetTexture And Get it��
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

	//����ɫ��2��fallback������һ����̥����Ⱦ������Subshader2: fallback(just main texture)��
	Subshader 
	{
		Pass 
		{
			SetTexture [_MainTex] { combine texture }
		}
	}

}
