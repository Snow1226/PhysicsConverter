## v.0.2.0  
- PhysboneのAllow Collisionを一旦手と指のみ対応。
- RootBoneを再調整
- InsideColliderをPlaneColliderで囲むことで暫定対応
- PhysboneとInsideColliderを使用したConstraintは再現ができないためAimConstraintに置き換え
- VRC Constraintの変換でAxis Lockが漏れていたので対応。
- Gravityが強めの場合の変換を調整。
- 
## v.0.1.4
- 変換前の不要オブジェクト削除を、名前で指定から一覧からチェックして削除に変更。
## v.0.1.3
- 出力前にAnimationControllerを空にするように変更。
## v.0.1.2
- コライダーのスケールがBakeAvatarで変更される可能性があるため、BakeAvatar後にコライダーのスケールを元に戻すように変更。
## v.0.1.1
- Initial release.