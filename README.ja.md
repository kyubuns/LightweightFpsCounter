# LightweightFpsCounter

**シンプルで高速な、Unity向けFPSカウンター。**

実行時負荷を下げることにこだわった、URP(Universal Render Pipeline)向けのミニマルなFPSカウンターです。

[English README is here](README.md)

<img width="332" height="162" alt="LightweightFpsCounter" src="https://github.com/user-attachments/assets/19db7233-5a9b-42a8-bd29-27e40de68377" />

`NOW` は最新の値、`AVG` は1秒間の平均値です。

## 特徴

- `FrameTimingManager` による計測で、FPSに加え CPU合計 / メインスレッド / Present待ち / レンダースレッド / GPU のフレームタイムを表示。
- メトリクスごとに表示/非表示を切り替えられ、すべてのラベルを自由に編集可能。
- FPSが閾値を下回った時やフレームタイムが閾値を上回った時などに、値を警告色/エラー色に変化させられます。
- NOWにまとめるフレーム数、文字スケール、四隅アンカー、マージン、各色をすべて調整可能。
- ビットマップフォントは任意のグリフ/セル寸法のアトラスに差し替え可能。
- `LightweightFpsCounterHud.LatestFps` などのstaticプロパティでコードから値も取得可能。
- 実行時のGCアロケーションはゼロ。

## 動作要件

- Unity 6.3以降。URP(Universal Render Pipeline)向けに開発していますが、パイプライン固有のコードは含みません。
- Player Settingsで「Frame Timing Stats」を有効化(無効の場合は全ての値が `0` になります)。
- 一部のプラットフォームでは取得できない値があります。詳しくは [FrameTimingManagerのドキュメント](https://docs.unity3d.com/ScriptReference/FrameTimingManager.html) をご覧ください。

## インストール

1. Package Managerの *Install package from git URL...* に `https://github.com/kyubuns/LightweightFpsCounter.git?path=Assets/LightweightFpsCounter` を入力してインストール。
2. 同梱の **LightweightFpsCounterHud** Prefabを最初のシーンに配置。

## リリースビルド

実装はエディタとDevelopment Buildでのみコンパイルされ、非Developmentビルドには空のスタブだけが残りアセットも含まれません。
リリースビルドでも意図的に使いたい場合は `FPS_COUNTER_ENABLE_IN_RELEASE` を定義してください。

## 速さの理由

- 単一メッシュ内で、ヘッダー・ラベル・`ms`の静的領域は設定変更時のみ再構築。数字は固定幅スロットとして保持。
- 指定フレーム数の計測値をまとめて取得し、その平均値でNOWを更新。
- NOWとAVGの数字スロットを連続領域に分離。通常更新ではNOW側のUV範囲だけを書き換え、検証をスキップする `MeshUpdateFlags` で部分アップロード。
- 頂点カラーの再アップロードは値が閾値をまたいだ時のみ。
- 静的文字と動的数字を、属性ごとに頂点ストリームを分けた単一メッシュへ格納。更新時は数字UVの範囲だけを転送。
- 描画命令はコマンドバッファへ事前記録し、フレーム終端では1回の `ExecuteCommandBuffer` のみ。16-bitインデックスを使用し、パイプラインへのフック・カリング・ソートもありません。
- 頂点シェーダーがピクセル座標を直接クリップ座標に変換するため、アンカー配置のCPUコストはゼロ。

## フォント

同梱フォントは datagoblin 氏による [monogram](https://datagoblin.itch.io/monogram) です(`Monogram.png`、CC0)。
ASCII 32..126を格納した任意のビットマップアトラスが利用可能です: **Glyph Size**、**Cell Size**、**Atlas Origin**、**Atlas Columns**、**Letter Spacing**、**Line Height** をテクスチャに合わせて調整してください。
カスタムアトラスはPointフィルタ、圧縮なし、ミップマップなし、Non-Power of 2をNoneにしてインポートしてください。

## クレジット

- コードは Claude Fable 5 と ChatGPT 5.6 を使用して生成されました。
- フォント: datagoblin 氏による [monogram](https://datagoblin.itch.io/monogram)(CC0)。
