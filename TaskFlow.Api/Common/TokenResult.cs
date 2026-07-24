namespace TaskFlow.Api.Common;

   /// <summary>A freshly minted JWT and the moment it expires.</summary>
   public record TokenResult(string Token, DateTime ExpiresAt);