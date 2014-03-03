#include "LightingCommon.fxh"
#include "Shadow2D.fxh"

// Lighting parameters
static const int NUM_DIRECTIONAL_LIGHTS = 3;
float3 DirectionalLightDirections[NUM_DIRECTIONAL_LIGHTS];
float3 DirectionalLightColors[NUM_DIRECTIONAL_LIGHTS];
float3 AmbientLightColor;

float4x4 ShadowViewProjectionMatrix;

float3 EnvironmentColor = float3(1, 1, 1);

textureCUBE EnvironmentTexture;
samplerCUBE EnvironmentSampler = sampler_state
{
	Texture = <EnvironmentTexture>;
	MinFilter = linear;
	MagFilter = linear;
	MipFilter = linear;
	AddressU = WRAP;
	AddressV = WRAP;
};

// Calculate the contribution of a directional light
LightingOutput CalcDirectionalLighting(
						float3 lightColor,
						float3 normal,
						float specularPower,
						float specularIntensity,
						float3 cameraToPoint,
						float3 lightDir,
						float3 reflectedViewRay)
{
	LightingOutput output;
	// Modulate the lighting terms based on the material colors, and the attenuation factor
	if (dot(normal, normal) < 0.01f)
		output.lighting = lightColor;
	else
	{
		output.lighting = lightColor * saturate(dot(normal, lightDir));
		output.specular = lightColor * pow(saturate(dot(reflectedViewRay, lightDir)), specularPower) * specularIntensity;
	}
	return output;
}

const float ShadowBias = 0.0013f;

void GlobalLightPS(	in PostProcessPSInput input,
					out float4 lighting : COLOR0,
					out float4 specular : COLOR1,
					uniform bool shadow)
{
	float4 normalValue = tex2D(SourceSampler1, input.texCoord);
	float3 normal = DecodeNormal(normalValue);
	if (normal.x * normal.y * normal.z == 0.0f)
	{
		lighting = (float4)0;
		specular = (float4)0;
	}
	else
	{
		float3 viewRay = normalize(input.viewRay);
		float3 worldPos = PositionFromDepthSampler(SourceSampler0, input.texCoord, viewRay);
		float specularPower = tex2D(SourceSampler2, input.texCoord).w * 255.0f;
		float specularIntensity = normalValue.w;

		float3 reflectedViewRay = reflect(viewRay, normal);

		LightingOutput output;
		output.lighting = AmbientLightColor;
		output.specular = float3(0, 0, 0);

		// Directional lights

		if (shadow)
		{
			// Shadowed light
			LightingOutput shadowLight = CalcDirectionalLighting(DirectionalLightColors[0],
									normal,
									specularPower,
									specularIntensity,
									viewRay,
									-DirectionalLightDirections[0],
									reflectedViewRay);
			float4 shadowPos = mul(float4(worldPos, 1.0f), ShadowViewProjectionMatrix);
			float shadowValue = GetShadowValue(shadowPos, ShadowBias);
			output.lighting += shadowLight.lighting * shadowValue;
			output.specular += shadowLight.specular * shadowValue;
		}

		[unroll]
		for(int i = shadow ? 1 : 0; i < NUM_DIRECTIONAL_LIGHTS; i++)
		{
			LightingOutput light = CalcDirectionalLighting(DirectionalLightColors[i],
									normal,
									specularPower,
									specularIntensity,
									viewRay,
									-DirectionalLightDirections[i],
									reflectedViewRay);
			output.lighting += light.lighting;
			output.specular += light.specular;
		}

		output.specular += texCUBE(EnvironmentSampler, reflectedViewRay).xyz * EnvironmentColor * specularIntensity * specularPower * (specularPower == 255.0f ? 1.0f : 0.25f / 255.0f);

		lighting.xyz = EncodeColor(output.lighting);
		lighting.w = 1.0f;
		specular.xyz = EncodeColor(output.specular);
		specular.w = 1.0f;
	}
}

technique GlobalLightShadow
{
	pass p0
	{
		VertexShader = compile vs_3_0 PostProcessVS();
		PixelShader = compile ps_3_0 GlobalLightPS(true);
		
		ZEnable = false;
		ZWriteEnable = false;
		AlphaBlendEnable = false;
	}
}

technique GlobalLight
{
	pass p0
	{
		VertexShader = compile vs_3_0 PostProcessVS();
		PixelShader = compile ps_3_0 GlobalLightPS(false);
		
		ZEnable = false;
		ZWriteEnable = false;
		AlphaBlendEnable = false;
	}
}