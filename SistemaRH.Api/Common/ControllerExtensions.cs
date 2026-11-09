using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;

namespace SistemaRH.Api.Common;

/// <summary>
/// Depois que o middleware de autenticação valida o token JWT, as
/// informações dele (claims) ficam disponíveis em "User" dentro do
/// Controller. Esse helper só poupa repetir esse código em todo endpoint
/// protegido.
/// </summary>
public static class ControllerExtensions
{
    public static int UsuarioIdAutenticado(this ControllerBase controller)
    {
        var claim = controller.User.FindFirst(JwtRegisteredClaimNames.Sub)
                    ?? controller.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

        if (claim is null || !int.TryParse(claim.Value, out var usuarioId))
        {
            throw new InvalidOperationException(
                "Não foi possível identificar o usuário autenticado a partir do token.");
        }

        return usuarioId;
    }
}
