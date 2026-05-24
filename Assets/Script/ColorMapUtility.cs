using UnityEngine;

/// <summary>
/// Utility class for generating colormaps
/// </summary>
public static class ColorMapUtility
{
    /// <summary>
    /// Generates a Jet colormap texture
    /// Jet gradient: Blue → Cyan → Green → Yellow → Red
    /// </summary>
    public static Texture2D GenerateJetColormap(int width = 125)
    {
        Texture2D texture = new Texture2D(width, 1, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        
        for (int i = 0; i < width; i++)
        {
            float t = i / (float)(width - 1);
            Color color = GetJetColor(t);
            texture.SetPixel(i, 0, color);
        }
        
        texture.Apply();
        return texture;
    }
    
    /// <summary>
    /// Get Jet color for normalized value [0, 1]
    /// </summary>
    public static Color GetJetColor(float t)
    {
        // Clamp to [0, 1]
        t = Mathf.Clamp01(t);
        
        float r, g, b;
        
        if (t < 0.125f)
        {
            // Dark blue to blue
            r = 0f;
            g = 0f;
            b = 0.5f + (t / 0.125f) * 0.5f;
        }
        else if (t < 0.375f)
        {
            // Blue to cyan
            r = 0f;
            g = ((t - 0.125f) / 0.25f);
            b = 1f;
        }
        else if (t < 0.625f)
        {
            // Cyan to green to yellow
            r = ((t - 0.375f) / 0.25f);
            g = 1f;
            b = 1f - ((t - 0.375f) / 0.25f);
        }
        else if (t < 0.875f)
        {
            // Yellow to red
            r = 1f;
            g = 1f - ((t - 0.625f) / 0.25f);
            b = 0f;
        }
        else
        {
            // Red to dark red
            r = 1f - ((t - 0.875f) / 0.125f) * 0.5f;
            g = 0f;
            b = 0f;
        }
        
        return new Color(r, g, b, 1f);
    }
}
