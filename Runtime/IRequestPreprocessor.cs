namespace NCV.Network.Http
{
    public interface IRequestPreprocessor
    {
        bool Preprocess(RequestContext context);
    }
}
