# 3. Mac で使う（コマンドライン）

Mac では、画面のあるエミュレータの代わりに、コマンドラインの `pokecom` が使えます。[16章](16_CLI.md)のコマンドと、AI のツールから使う [MCP サーバー](18_MCP.md)は、Windows・Linux と同じです。

## 動作環境

| 項目 | 内容 |
|---|---|
| OS | macOS 15 以降（14 でも動く見込み） |
| CPU | Apple シリコン、Intel |
| 入れておくもの | なし（.NET のランタイムを同梱しています） |

## 入れかた

[リリース](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases)から、CPU に合うものを入手します。

| Mac | ファイル |
|---|---|
| Apple シリコン（M1 以降） | `pokecom-<版>-osx-arm64.tar.gz` |
| Intel | `pokecom-<版>-osx-x64.tar.gz` |

ターミナルで展開し、初めに一度だけ、ダウンロードしたファイルの実行の制限を外します。Apple の公証を受けていないため、そのままでは macOS が実行を止めます。

```sh
tar xzf pokecom-<版>-osx-arm64.tar.gz
cd pokecom-<版>-osx-arm64
xattr -dr com.apple.quarantine .
./pokecom version
```

どこからでも使うには、PATH の通った場所にリンクを作ります。

```sh
sudo mkdir -p /usr/local/bin
sudo ln -s "$PWD/pokecom" /usr/local/bin/pokecom
```

`pokecom` と `libe500core.dylib` は、同じフォルダに置いたままにしてください。

## ROM を置く

[実機から吸い出した ROM](04_ROM.md) を、次のどれかに置きます。

- `pokecom` のあるフォルダの `rom`
- `~/Library/Application Support/PockeComE500/rom`
- いまいるフォルダの `rom`

```sh
./pokecom roms                 # 見つかった ROM
./pokecom exec "PRINT 12*34"   # 動かしてみる
```

別の場所の ROM は、`--rom` か環境変数 `POKECOM_ROM` で指定します。

```sh
./pokecom exec "PRINT 1+2" --rom "$HOME/PC-E500(7.2).ROM"
```

## 次に読む章

- [16章 コマンドライン](16_CLI.md)
- [18章 MCP サーバー](18_MCP.md)

---

[← 2. Ubuntu（Linux）で使う](02_LINUX.md) ｜ [目次](../README.md) ｜ [4. ROM を用意する →](04_ROM.md)
