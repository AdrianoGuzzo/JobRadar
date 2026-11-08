namespace JobRadar.Domain.Common;

/// <summary>Violação de uma regra de negócio. A mensagem é legível pelo usuário.</summary>
public class DomainException(string message) : Exception(message);
