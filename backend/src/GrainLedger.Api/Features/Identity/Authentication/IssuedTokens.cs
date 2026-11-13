namespace GrainLedger.Api.Features.Identity;

public sealed record IssuedTokens(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, SessionUserDto User);
