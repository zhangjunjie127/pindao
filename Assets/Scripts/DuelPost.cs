using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class DuelPost : MonoBehaviour
{
    public float intensity, damage, flash;
    public float lines, ink, bars;
    public Vector2 focus = new Vector2(.5f, .5f);
    Material material;
    void Awake() { material = new Material(Resources.Load<Shader>("DuelPost")); }
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        material.SetFloat("_Pulse", intensity); material.SetFloat("_Damage", damage); material.SetFloat("_Flash", flash);
        material.SetFloat("_Lines", lines); material.SetFloat("_Ink", ink); material.SetFloat("_Bars", bars);
        material.SetVector("_Focus", new Vector4(focus.x, focus.y, 0, 0));
        Graphics.Blit(source, destination, material);
    }
    void OnDestroy() { if (material) Destroy(material); }
}
