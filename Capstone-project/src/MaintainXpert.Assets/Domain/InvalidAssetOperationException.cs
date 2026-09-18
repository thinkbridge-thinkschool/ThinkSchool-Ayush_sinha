namespace MaintainXpert.Assets.Domain;

public sealed class InvalidAssetOperationException : Exception
{
    public InvalidAssetOperationException(string message) : base(message)
    {
    }
}
