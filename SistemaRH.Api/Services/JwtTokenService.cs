using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

/// <summary>
/// Configurações do JWT lidas do appsettings.json (seção "Jwt").
/// </summary>
public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiracaoEmMinutos { get; set; } = 120;
}

/// <summary>
/// Gera o "crachá digital" (token JWT) que o usuário usa pra provar, em cada
/// requisição seguinte, que já fez login — sem precisar mandar e-mail/senha
/// de novo a cada chamada.
///
/// Um JWT tem 3 partes (cabeçalho.corpo.assinatura). O "corpo" contém
/// informações do usuário (claims), e a "assinatura" é calculada com uma
/// chave secreta que só o servidor conhece — é isso que garante que ninguém
/// consiga forjar ou alterar um token válido.
/// </summary>
public interface IJwtTokenService
{
    string GerarToken(Usuario usuario);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GerarToken(Usuario usuario)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim("nome", usuario.Nome),
            // Usada pela policy "RhOuAdmin" (ver Program.cs) pra restringir as
            // rotas administrativas/RH a quem tem o perfil certo.
            new Claim("perfil", usuario.Perfil.ToString()),
        };

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credenciais = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiracaoEmMinutos),
            signingCredentials: credenciais
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
