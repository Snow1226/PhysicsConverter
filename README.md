## Physics Converter  
VRC用にセットアップしたアバターのPhysboneをMagicaCloth2に変換し、  
VirtualMotionCaptureで使用するためのエクスポーターです。  
  
MagicaClothはPhysboneのHinge、Polarに該当する角度制限がないため、スカートなどの貫通具合も変わります。  
貫通が目立つ場合は手動でMeshClothを設定してください。

現在は揺れ具合はPhysboneより少し柔らか目になるように変換しています。  
  
揺れ具合ではなく明らかに向き等がおかしい場合は、  
使用しているアバターと衣装のBOOTH URL、どの部分がおかしいか等を添えてIssueを立てていただけると検討するかもしれません。  
  
### 必要なもの
- [MagicaCloth2](https://assetstore.unity.com/packages/tools/physics/magica-cloth-2-242307)   
- [ModularAvatar](https://modular-avatar.nadena.dev/ja)  
プロジェクトに事前にインポートしておいてください。

### Install
VRChat Creater CompanionまたはALCOMをインストールし、  
以下のリンクをクリックし、Add to VCCを押してVPMリポジトリを追加した後、  
プロジェクトにPhysicConverterをインポートしてください。  
[VPM Repository](https://snow1226.github.io/vpm-repos/)  

### 使い方
UnityProjectを開き、ツールバーのTool→Neigerium→Physics Converter(Physbone to MagicaCloth2)を選択し、変換ウインドウを開いてください。  
Target Avatarに変換したいVRC用アバターをドラッグ＆ドロップで入れるとボタンが出てきます。  
- Convert & Export Avatar : 変換から出力までを行います。  
- Convert Avatar : 変換のみを行います。揺れ具合や貫通の確認・調整をしたい場合はこちらを押してください。  
  
TargetAvatarにConvertAvatarで変換したアバターを入れるとExport Avatarのボタンが出てきますので、こちらを押すとアバターデータの出力が行われます。  

- DestroyObjectSetting : アバター直下でVRC以外では不要なオブジェクトを選択します。（例：LightLimiｔChanger・VirtualLens等）  
事前にEditorOnlyにしてあれば選択する必要はありません。

### MagicaCloth2のMeshClothについて
ほぼ確実に貫通を防げるMeshClothについてはスクリプトから設定することができないため、  
必要に応じて手動で設定してください。
基本的にコライダーと揺れ具合はそれっぽく転送されていますので、スカートについているMagicaClothをMeshClothに変更し、
揺れる箇所のペイントを行うだけで貫通を防げるようになると思います。
 
### ChilloutVR、Warudoでの使用について
要求Unityバージョンが異なるため、MagicaCloth2へ変換後にそれぞれのプロジェクトにアバターデータを移す必要があります。  
そのままでは利用できませんのでご注意ください。
