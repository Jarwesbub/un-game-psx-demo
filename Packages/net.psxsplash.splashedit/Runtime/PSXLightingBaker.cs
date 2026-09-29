using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    public static class PSXLightingBaker
    {
        /// <summary>
        /// Computes the per-vertex lighting from all scene light sources.
        /// Incorporates ambient, diffuse, and spotlight falloff.
        /// </summary>
        /// <param name="vertex">The world-space position of the vertex.</param>
        /// <param name="normal">The normalized world-space normal of the vertex.</param>
        /// <param name="sceneLights">Pre-gathered array of enabled scene lights (pass to avoid per-vertex FindObjectsByType).</param>
        /// <returns>A Color representing the lit vertex.</returns>
        public static Color ComputeLighting(Vector3 vertex, Vector3 normal, Light[] sceneLights)
        {
            Color finalColor = Color.black;

            foreach (Light light in sceneLights)
            {
                Color lightContribution = Color.black;

                if (light.type == LightType.Directional)
                {
                    Vector3 lightDir = -light.transform.forward;
                    float NdotL = Mathf.Max(0f, Vector3.Dot(normal, lightDir));
                    lightContribution = light.color * light.intensity * NdotL;
                }
                else if (light.type == LightType.Point)
                {
                    Vector3 lightDir = light.transform.position - vertex;
                    float distance = lightDir.magnitude;
                    lightDir.Normalize();

                    float NdotL = Mathf.Max(0f, Vector3.Dot(normal, lightDir));
                    float attenuation = 1.0f / Mathf.Max(distance * distance, 0.0001f);
                    lightContribution = light.color * light.intensity * NdotL * attenuation;
                }
                else if (light.type == LightType.Spot)
                {
                    Vector3 L = light.transform.position - vertex;
                    float distance = L.magnitude;
                    L = L / distance; 

                    float NdotL = Mathf.Max(0f, Vector3.Dot(normal, L));
                    float attenuation = 1.0f / Mathf.Max(distance * distance, 0.0001f);

                    float outerAngleRad = (light.spotAngle * 0.5f) * Mathf.Deg2Rad;
                    float innerAngleRad = outerAngleRad * 0.8f;

                    if (light is Light spotLight)
                    {
                        if (spotLight.innerSpotAngle > 0)
                        {
                            innerAngleRad = (spotLight.innerSpotAngle * 0.5f) * Mathf.Deg2Rad;
                        }
                    }

                    float cosOuter = Mathf.Cos(outerAngleRad);
                    float cosInner = Mathf.Cos(innerAngleRad);
                    float cosAngle = Vector3.Dot(L, -light.transform.forward);

                    if (cosAngle >= cosOuter)
                    {
                        float spotFactor = Mathf.Clamp01((cosAngle - cosOuter) / (cosInner - cosOuter));
                        spotFactor = Mathf.Pow(spotFactor, 4.0f);

                        lightContribution = light.color * light.intensity * NdotL * attenuation * spotFactor;
                    }
                    else
                    {
                        lightContribution = Color.black;
                    }
                }

                finalColor += lightContribution;
            }

            // Clamp to 0.8 to leave headroom for PS1 2x color blending mode,
            // which doubles vertex colors. Without this cap, bright areas would clip.
            finalColor.r = Mathf.Clamp(finalColor.r, 0.0f, 0.8f);
            finalColor.g = Mathf.Clamp(finalColor.g, 0.0f, 0.8f);
            finalColor.b = Mathf.Clamp(finalColor.b, 0.0f, 0.8f);
            finalColor.a = 1f;

            return finalColor;
        }
    }
}
