# 3. Mac で使う（コマンドライン）

Mac では画面のあるエミュレータの代わりに、コマンドラインの `pokecom` が使えます。[16章](16_CLI.md)のコマンドと、AI のツールから使う [MCP サーバー](18_MCP.md)は、Windows・Linux と同じです。

## 動作環境

| 項目 | 内容 |
|---|---|
| OS | macOS 15 以降（14 でも動く見込み） |
| CPU | Apple シリコン、Intel |
| 入れておくもの | なし（.NET のランタイムを同梱しています） |

## 入れかた

「ターミナル」（「アプリケーション」→「ユーティリティ」）を開いて、次の1行を貼り付けます。いちばん新しい版を GitHub から取って、入れ終わりまで進みます。新しくするときも同じ行をもう一度実行します（ROM はそのまま残ります）。

```sh
curl -fsSL https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh | sh
```

`curl -fsSL アドレス` が、インストール用のスクリプト `install.sh` を GitHub から取り、`| sh` がそれを実行します。オプションは `| sh -s --` の後ろに書きます。自分の ROM（例 `~/PC-E500(7.2).ROM`）も一緒に置くなら、次のようにします。

```sh
curl -fsSL https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh | sh -s -- --rom ~/'PC-E500(7.2).ROM'
```

中身を見てから実行したいときは、次のように分けます。

```sh
curl -fsSL https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh > install.sh
less install.sh
sh install.sh
```

| コマンド | 何をするか |
|---|---|
| `curl -fsSL アドレス > install.sh` | スクリプトを取って、いまいるフォルダに保存する |
| `less install.sh` | 中身を見る（`q` で終わる） |
| `sh install.sh` | 実行する |

| オプション | 意味 |
|---|---|
| `--rom ファイル` | 自分の ROM を `rom` フォルダにコピーする。名前の `( )` はシェルの記号なので `'…'` で囲む |
| `--emulator ファイル` | 取ってくる代わりに、手元の `.tar.gz` を使う |

### 入るもの

| もの | 場所 |
|---|---|
| `pokecom` と `libe500core.dylib`（同じフォルダに置いたままにします） | `~/pockecom/Emulator` |
| `pokecom` と打つためのファイル | `~/.local/bin/pokecom` |
| ROM | `~/pockecom/Emulator/rom` |

- `sudo` を付けずに実行します。自分のフォルダだけに入れるので、管理者の権限は要りません。Apple シリコンか Intel かは、スクリプトが調べます。
- `~/.local/bin` が PATH（コマンドを探すフォルダの一覧）になければ、スクリプトが `~/.zshrc` に1行足します。**新しいターミナルを開くと**、`pokecom` が打てるようになります。
- `~/pockecom` を別の場所にするには、`POCKECOM_HOME=フォルダ sh install.sh` のように書きます。
- 置き場所は[PC-E500 開発ツール](https://github.com/litchi-gh/pc-e500_tools)の `install.sh` と同じです。そちらで入れたエミュレータはそのまま使えます。
- ブラウザで取ったファイルには macOS の「実行の制限」の印が付きます（Apple の公証を受けていないため）。`--emulator` で入れれば、スクリプトが外します。

入ったかどうかを確かめます。

```sh
pokecom version
pokecom roms
```

| コマンド | 何をするか | 正しいとき |
|---|---|---|
| `pokecom version` | 版を表示する | `0.1.0` など |
| `pokecom roms` | 見つかった ROM を並べる | 自分の ROM が出る |

### アンインストール

```sh
rm -r ~/pockecom/Emulator
rm ~/.local/bin/pokecom
```

`rm -r フォルダ` はフォルダを中身ごと、`rm ファイル` はファイルを消します。`rom` に置いた ROM も消えるので、先に移してください。

## ROM を置く

`--rom` を付けて入れたときは、もう置いてあります。あとから置くときは、[実機から吸い出した ROM](04_ROM.md) を次のどれかに置きます。

- `pokecom` のあるフォルダの `rom`（`~/pockecom/Emulator/rom`）
- `~/Library/Application Support/PockeComE500/rom`
- いまいるフォルダの `rom`

自分の ROM が `~/PC-E500(7.2).ROM`（ホーム）にある例です。`cp 元 先` はファイルをコピーします。名前の `( )` はシェルの記号なので `'…'` で囲みます。

```sh
cp ~/'PC-E500(7.2).ROM' ~/pockecom/Emulator/rom/
```

`pokecom roms` で見つかった ROM の一覧が出ます。

```sh
pokecom roms
pokecom exec "PRINT 12*34"
```

別の場所の ROM は `--rom` か環境変数 `POKECOM_ROM` で指定します。

```sh
pokecom exec "PRINT 1+2" --rom "$HOME/PC-E500(7.2).ROM"
```

## 次に読む章

- [16章 コマンドライン](16_CLI.md)
- [18章 MCP サーバー](18_MCP.md)

---

[← 2. Ubuntu（Linux）で使う](02_LINUX.md) ｜ [目次](../README.md) ｜ [4. ROM を用意する →](04_ROM.md)
