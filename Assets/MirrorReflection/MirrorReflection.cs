using UnityEngine;
using System.Collections;

//其实这个脚本就是去掉折射后的水面渲染效果脚本|| This is in fact just the Water script with refraction stuff removed.


//使镜面实时更新||Make mirror live-update even when not in play mode
[ExecuteInEditMode] 
public class MirrorReflection : MonoBehaviour
{
    //一些参数定义||Some Variable Definition
    public bool m_DisablePixelLights = true;
    public int m_TextureSize = 256;
    public float m_ClipPlaneOffset = 0.07f;
    
    public LayerMask m_ReflectLayers = -1;

    private Hashtable m_ReflectionCameras = new Hashtable(); // 用一下哈希表，摄像机对应摄像机的键值（Camera -> Camera table）  
    
    private RenderTexture m_ReflectionTexture = null;
    private int m_OldReflectionTextureSize = 0;
    
    private static bool s_InsideRendering = false;

    //该函数在知道物体会被某相机渲染后被调用
    // This is called when it's known that the object will be rendered by some
    // camera. We render reflections and do other updates here.
    // Because the script executes in edit mode, reflections for the scene view
    // camera will just work!
    public void OnWillRenderObject( )
    {
        if( !enabled || !renderer || !renderer.sharedMaterial || !renderer.enabled )
            return;
            
        Camera cam = Camera.current;
        if( !cam )
            return;

        // 判断一下是否在渲染||Safeguard from recursive reflections.
        if( s_InsideRendering )
            return;

        s_InsideRendering = true;
        
        Camera reflectionCamera;
        CreateMirrorObjects( cam, out reflectionCamera );
        
        // 找到反射平面||find out the reflection plane: position and normal in world space
        Vector3 pos = transform.position;
        Vector3 normal = transform.up;

        // 可选禁用像素光源反射||Optionally disable pixel lights for reflection
        int oldPixelLightCount = QualitySettings.pixelLightCount;
        if( m_DisablePixelLights )
            QualitySettings.pixelLightCount = 0;
        
        UpdateCameraModes( cam, reflectionCamera );
        
        // 渲染反射||Render reflection
        // 反射平面旁边的用于渲染反射图像的摄像机||Reflect camera around reflection plane
        float d = -Vector3.Dot (normal, pos) - m_ClipPlaneOffset;
        Vector4 reflectionPlane = new Vector4 (normal.x, normal.y, normal.z, d);
    
        Matrix4x4 reflection = Matrix4x4.zero;
        CalculateReflectionMatrix (ref reflection, reflectionPlane);
        Vector3 oldpos = cam.transform.position;
        Vector3 newpos = reflection.MultiplyPoint( oldpos );
        reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

        // 设置斜投影矩阵||Setup oblique projection matrix so that near plane is our reflection
        // plane. This way we clip everything below/above it for free.
        Vector4 clipPlane = CameraSpacePlane( reflectionCamera, pos, normal, 1.0f );
        Matrix4x4 projection = cam.projectionMatrix;
        CalculateObliqueMatrix (ref projection, clipPlane);
        reflectionCamera.projectionMatrix = projection;
        
        reflectionCamera.cullingMask = ~(1<<4) & m_ReflectLayers.value; // never render water layer
        reflectionCamera.targetTexture = m_ReflectionTexture;
        GL.SetRevertBackfacing (true);
        reflectionCamera.transform.position = newpos;
        Vector3 euler = cam.transform.eulerAngles;
        reflectionCamera.transform.eulerAngles = new Vector3(0, euler.y, euler.z);
        reflectionCamera.Render();
        reflectionCamera.transform.position = oldpos;
        GL.SetRevertBackfacing (false);
        Material[] materials = renderer.sharedMaterials;
        foreach( Material mat in materials ) {
            if( mat.HasProperty("_ReflectionTex") )
                mat.SetTexture( "_ReflectionTex", m_ReflectionTexture );
        }

        // 从物体空间到屏幕材质的Shader设置矩阵||Set matrix on the shader that transforms UVs from object space into screen
        // space. We want to just project reflection texture on screen.
        Matrix4x4 scaleOffset = Matrix4x4.TRS(
            new Vector3(0.5f,0.5f,0.5f), Quaternion.identity, new Vector3(0.5f,0.5f,0.5f) );
        Vector3 scale = transform.lossyScale;
        Matrix4x4 mtx = transform.localToWorldMatrix * Matrix4x4.Scale( new Vector3(1.0f/scale.x, 1.0f/scale.y, 1.0f/scale.z) );
        mtx = scaleOffset * cam.projectionMatrix * cam.worldToCameraMatrix * mtx;
        foreach( Material mat in materials ) {
            mat.SetMatrix( "_ProjMatrix", mtx );
        }
        
        // Restore pixel light count
        if( m_DisablePixelLights )
            QualitySettings.pixelLightCount = oldPixelLightCount;
        
        s_InsideRendering = false;
    }
    
    
    // Cleanup all the objects we possibly have created
    void OnDisable( )
    {
        if( m_ReflectionTexture ) 
        {
            DestroyImmediate( m_ReflectionTexture );
            m_ReflectionTexture = null;
        }
        foreach( DictionaryEntry kvp in m_ReflectionCameras )
            DestroyImmediate( ((Camera)kvp.Value).gameObject );
        m_ReflectionCameras.Clear();
    }
    
    
    private void UpdateCameraModes( Camera src, Camera dest )
    {
        if( dest == null )
            return;
        // set camera to clear the same way as current camera
        dest.clearFlags = src.clearFlags;
        dest.backgroundColor = src.backgroundColor;        
        if( src.clearFlags == CameraClearFlags.Skybox )
        {
            Skybox sky = src.GetComponent(typeof(Skybox)) as Skybox;
            Skybox mysky = dest.GetComponent(typeof(Skybox)) as Skybox;
            if( !sky || !sky.material )
            {
                mysky.enabled = false;
            }
            else
            {
                mysky.enabled = true;
                mysky.material = sky.material;
            }
        }
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }
    
    // 创建镜像物体||On-demand create any objects we need
    private void CreateMirrorObjects( Camera currentCamera, out Camera reflectionCamera )
    {
        reflectionCamera = null;

        // 反射渲染纹理||Reflection render texture
        if( !m_ReflectionTexture || m_OldReflectionTextureSize != m_TextureSize )
        {
            if( m_ReflectionTexture )
                DestroyImmediate( m_ReflectionTexture );
            m_ReflectionTexture = new RenderTexture( m_TextureSize, m_TextureSize, 16 );
            m_ReflectionTexture.name = "__MirrorReflection" + GetInstanceID();
            m_ReflectionTexture.isPowerOfTwo = true;
            m_ReflectionTexture.hideFlags = HideFlags.DontSave;
            m_OldReflectionTextureSize = m_TextureSize;
        }
        
        // 用于镜面反射的摄像机||Camera for reflection
        reflectionCamera = m_ReflectionCameras[currentCamera] as Camera;
        if( !reflectionCamera ) // 判断是否在字典之内，并相应的进行操作||catch both not-in-dictionary and in-dictionary-but-deleted-GO
        {
            GameObject go = new GameObject( "Mirror Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox) );
            reflectionCamera = go.camera;
            reflectionCamera.enabled = false;
            reflectionCamera.transform.position = transform.position;
            reflectionCamera.transform.rotation = transform.rotation;
            reflectionCamera.gameObject.AddComponent("FlareLayer");
            go.hideFlags = HideFlags.HideAndDontSave;
            m_ReflectionCameras[currentCamera] = reflectionCamera;
        }        
    }

    // 标识符，根据a的不同值返回1, 0 or 1（Extended sign: returns -1, 0 or 1 based on sign of a）
    private static float sgn(float a)
    {
        if (a > 0.0f) return 1.0f;
        if (a < 0.0f) return -1.0f;
        return 0.0f;
    }

    // 给定平面的位置/法线向量，计算摄像机空间平面（Given position/normal of the plane, calculates plane in camera space.）
    private Vector4 CameraSpacePlane (Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint( offsetPos );
        Vector3 cnormal = m.MultiplyVector( normal ).normalized * sideSign;
        return new Vector4( cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos,cnormal) );
    }

    // 调整给定的投影矩阵，所以近平面就是剪切平面。（Adjusts the given projection matrix so that near plane is the given clipPlane）
    // 剪切平面在相机空间里给出了，具体可以参考《游戏编程精粹5》中的一篇文章。（clipPlane is given in camera space. See article in Game Programming Gems 5.）
    private static void CalculateObliqueMatrix (ref Matrix4x4 projection, Vector4 clipPlane)
    {
        Vector4 q = projection.inverse * new Vector4
        ( sgn(clipPlane.x),
            sgn(clipPlane.y),
            1.0f,
            1.0f);

        Vector4 c = clipPlane * (2.0F / (Vector4.Dot (clipPlane, q)));
        // 第三行=剪切平面-第四行（third row = clip plane - fourth row）
        projection[2] = c.x - projection[3];
        projection[6] = c.y - projection[7];
        projection[10] = c.z - projection[11];
        projection[14] = c.w - projection[15];
    }

    // 计算给定平面的反射矩阵（Calculates reflection matrix around the given plane）
    private static void CalculateReflectionMatrix (ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F*plane[0]*plane[0]);
        reflectionMat.m01 = (   - 2F*plane[0]*plane[1]);
        reflectionMat.m02 = (   - 2F*plane[0]*plane[2]);
        reflectionMat.m03 = (   - 2F*plane[3]*plane[0]);

        reflectionMat.m10 = (   - 2F*plane[1]*plane[0]);
        reflectionMat.m11 = (1F - 2F*plane[1]*plane[1]);
        reflectionMat.m12 = (   - 2F*plane[1]*plane[2]);
        reflectionMat.m13 = (   - 2F*plane[3]*plane[1]);
    
        reflectionMat.m20 = (   - 2F*plane[2]*plane[0]);
        reflectionMat.m21 = (   - 2F*plane[2]*plane[1]);
        reflectionMat.m22 = (1F - 2F*plane[2]*plane[2]);
        reflectionMat.m23 = (   - 2F*plane[3]*plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }
}
