namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Версия пакета SDK — поле <c>sdk_version</c> каждого события <c>/v1/events</c> и
    /// <c>AmznGoDSDKCore.SdkVersion</c>. Единственный источник версии в рантайме: package.json в
    /// сборку игры не попадает. Релизный пайплайн (Editor/Deploy, SdkReleasePipeline) проставляет
    /// сюда версию релиза вместе с package.json, а верификатор релизного дерева сверяет обе.
    /// В dev-ветке держите здесь версию следующего релиза.
    /// </summary>
    public static class SdkPackageVersion
    {
        public const string Value = "1.0.9";
    }
}
