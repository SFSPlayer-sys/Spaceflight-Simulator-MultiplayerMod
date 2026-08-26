namespace MultiplayerSFS.Server
{
    //插件接口
    public interface IPlugin
    {
        string ID { get; }
        string Name { get; }
        string Author { get; }
        string Version { get; }
        string MinimumServerVersion { get; }
        void OnLoad();
        void OnUnload();
        void OnTick();
    }
}
