using Volo.Abp;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// Thrown when <see cref="INumberGenerator"/> is called outside a transactional unit of work: without the caller's
/// transaction a number would be committed apart from the record it numbers, and a rollback of the record would lose
/// the number (design 49, "Generation").
/// </summary>
public class NumberGenerationRequiresTransactionException : AbpException
{
    public NumberGenerationRequiresTransactionException()
        : base("INumberGenerator.GetNextAsync may only be called inside a transactional unit of work (an application-service " +
               "method that changes data), so the number is committed or rolled back with the record it numbers.")
    {
    }
}
