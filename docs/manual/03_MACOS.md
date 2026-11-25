# 3. Mac で使う（コマンドライン）

Mac では、画面のあるエミュレータの代わりに、コマンドラインの `pokecom` が使えます。[16章](16_CLI.md)のコマンドと、AI のツールから使う [MCP サーバー](18_MCP.md)は、Windows・Linux と同じです。

## 動作環境

| 項目 | 内容 |
|---|---|
| OS | macOS 15 以降（14 でも動く見込み） |
| CPU | Apple シリコン、Intel |
| 入れておくもの | なし（.NET のランタイムを同梱しています） |

## 入れかた

「ターミナル」（「アプリケーション」→「ユーティリティ」）に打つ（貼り付ける）だけで、取ってくる所から入れ終わりまで進みます。`get` の行は長いので、コピーして貼り付けます。

```sh
mkdir -p ~/pockecom/download
get() { curl -fL -o "$3" "$(curl -fsSL "https://api.github.com/repos/litchi-gh/$1/releases/latest" | grep -o "\"browser_download_url\": *\"[^\"]*$2\"" | cut -d'"' -f4)"; }
```

| コマンド | 何をするか |
|---|---|
| `mkdir -p フォルダ` | 取ったものを置くフォルダを作る。既にあればそのまま |
| `get() { … }` | 「GitHub の最新のリリースから、名前の終わりが合うファイルを取る」という自分用のコマンド `get` を作る。版を調べなくて済む。そのターミナルを閉じるまで使える。新しいターミナルでは、この行をもう一度貼り付ける |

`curl: (2)` か `curl: option : blank argument` と出たら、名前の合うファイルが最新のリリースにありません。[リリース](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases)のページで確かめます。

```sh
mkdir -p ~/pockecom/Emulator
ARCH=$(uname -m | sed 's/x86_64/x64/')
get pc-e500_emulator_pockecom "osx-$ARCH.tar.gz" ~/pockecom/download/emulator.tar.gz
tar xzf ~/pockecom/download/emulator.tar.gz -C ~/pockecom/Emulator --strip-components=1
~/pockecom/Emulator/pokecom version
```

| コマンド | 何をするか |
|---|---|
| `mkdir -p フォルダ` | 置き場所のフォルダを作る |
| `ARCH=…` | Mac の種類を調べて、Apple シリコンなら `arm64`、Intel なら `x64` を `ARCH` に入れる |
| `get …` | 最新版を取る。Mac の種類に合うもの |
| `tar xzf ファイル -C フォルダ` | `.tar.gz` を展開する。`x` は取り出す、`z` は gzip の圧縮を解く、`f` は次にファイル名を書くという意味。`-C` は取り出す場所、`--strip-components=1` は中にある版付きのフォルダを1段省く |
| `~/pockecom/Emulator/pokecom version` | 版を表示する。番号が出れば入っている |

置き場所と `get` は、[PC-E500 開発ツール](https://github.com/litchi-gh/pc-e500_tools)の入れ方と同じです。両方使うときは、どちらか一方の手順で入れれば足ります。

`curl` で取ったファイルには、macOS の「実行の制限」の印が付きません。ブラウザで取ったものを使うときは、展開したあとに `xattr -dr com.apple.quarantine ~/pockecom/Emulator` で印を外します（Apple の公証を受けていないため）。

### どこからでも使えるようにする

```sh
echo 'export PATH="$PATH:$HOME/pockecom/Emulator"' >> ~/.zshrc
source ~/.zshrc
pokecom version
```

| コマンド | 何をするか |
|---|---|
| `echo '…' >> ~/.zshrc` | ターミナルを開いたときに読まれる `~/.zshrc` の最後に1行足す。ここでは PATH（コマンドを探すフォルダの一覧）に、`pokecom` のフォルダを足している。**何度も打つと、その数だけ行が増える** |
| `source ~/.zshrc` | 今のターミナルで `~/.zshrc` を読み直す。打たなくても、次に開いたターミナルからは効く |
| `pokecom version` | どこからでも使えるかを確かめる |

### 更新とアンインストール

新しくするときは、`get` の行を貼り付け、`get …` と `tar …` をもう一度打ちます。`rom` に置いた ROM はそのまま残ります。

消すときは、`~/pockecom/Emulator` を消し、`~/.zshrc` に足した行を消します。`rom` に置いた ROM も消えるので、先に移してください。

`pokecom` と `libe500core.dylib` は、同じフォルダに置いたままにしてください。

## ROM を置く

[実機から吸い出した ROM](04_ROM.md) を、次のどれかに置きます。

- `pokecom` のあるフォルダの `rom`（`~/pockecom/Emulator/rom`）
- `~/Library/Application Support/PockeComE500/rom`
- いまいるフォルダの `rom`

自分の ROM が `~/PC-E500(7.2).ROM`（ホーム）にある例です。`cp 元 先` は、ファイルをコピーします。名前の `( )` はシェルの記号なので `'…'` で囲みます。

```sh
cp ~/'PC-E500(7.2).ROM' ~/pockecom/Emulator/rom/
```

`pokecom roms` で、見つかった ROM の一覧が出ます。

```sh
pokecom roms
pokecom exec "PRINT 12*34"
```

別の場所の ROM は、`--rom` か環境変数 `POKECOM_ROM` で指定します。

```sh
pokecom exec "PRINT 1+2" --rom "$HOME/PC-E500(7.2).ROM"
```

## 次に読む章

- [16章 コマンドライン](16_CLI.md)
- [18章 MCP サーバー](18_MCP.md)

---

[← 2. Ubuntu（Linux）で使う](02_LINUX.md) ｜ [目次](../README.md) ｜ [4. ROM を用意する →](04_ROM.md)
