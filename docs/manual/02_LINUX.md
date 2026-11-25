# 2. Ubuntu（Linux）で使う

Ubuntu のデスクトップで、Windows 版と同じ操作のエミュレータが動きます。ROM の処理も、保存するファイルの形式も同じです。

![Ubuntu で動かしたところ](../images/linux-main.png)

## 動作環境

| 項目 | 内容 |
|---|---|
| OS | Ubuntu 24.04 以降（x86-64）。デスクトップは Wayland でも X11 でもかまいません（Wayland では XWayland を通して動きます） |
| 入れておくもの | .NET のランタイムは同梱しています。`.deb` で入れると、足りないライブラリ（X11・フォント・ALSA など）も一緒に入ります |
| フォント | 日本語の表示に Noto Sans CJK を使います（`fonts-noto-cjk`。Ubuntu の日本語の環境には入っています） |
| ディスク | 約 180MB |

## 入れかた

端末に打つ（貼り付ける）だけで、取ってくる所から入れ終わりまで進みます。`get` の行は長いので、コピーして貼り付けます。

```sh
sudo apt update
sudo apt install -y curl
mkdir -p ~/pockecom/download
get() { curl -fL -o "$3" "$(curl -fsSL "https://api.github.com/repos/litchi-gh/$1/releases/latest" | grep -o "\"browser_download_url\": *\"[^\"]*$2\"" | cut -d'"' -f4)"; }
```

| コマンド | 何をするか |
|---|---|
| `sudo apt update` | 入れられるソフトの一覧を新しくする。まだ何も入れない |
| `sudo apt install -y curl` | ファイルを取ってくる `curl` を入れる。`-y` は、確認の質問に「はい」と答える |
| `mkdir -p フォルダ` | 取ったものを置くフォルダを作る。既にあればそのまま |
| `get() { … }` | 「GitHub の最新のリリースから、名前の終わりが合うファイルを取る」という自分用のコマンド `get` を作る。版を調べなくて済む。その端末を閉じるまで使える。新しい端末では、この行をもう一度貼り付ける |

続けて、次の**どちらか**で入れます。`curl: (2)` か `curl: option : blank argument` と出たら、名前の合うファイルが最新のリリースにありません。[リリース](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases)のページで確かめます。

置き場所と `get` は、[PC-E500 開発ツール](https://github.com/litchi-gh/pc-e500_tools)の入れ方と同じです。両方使うときは、どちらか一方の手順で入れれば足ります。

### A. インストールして使う（.deb）

```sh
get pc-e500_emulator_pockecom amd64.deb ~/pockecom/download/emulator.deb
sudo apt install ~/pockecom/download/emulator.deb
```

| コマンド | 何をするか |
|---|---|
| `get …` | エミュレータの最新版のパッケージを取る |
| `sudo apt install ファイル.deb` | パッケージのファイルから入れる。足りないライブラリ（X11・フォント・ALSA など）も一緒に入る |

アプリの一覧に「PC-E500 Emulator」が加わります。端末からは `pc-e500` で起動します。コマンドラインの [`pokecom`](16_CLI.md) も入ります。

| もの | 場所 |
|---|---|
| プログラム | `/opt/pc-e500/` |
| データ（ROM・RAM カード・X: のフォルダ・設定） | `~/.local/share/PockeComE500/`（利用者ごと） |

### B. 展開して使う（tar.gz）

```sh
mkdir -p ~/pockecom/Emulator
get pc-e500_emulator_pockecom linux-x64.tar.gz ~/pockecom/download/emulator.tar.gz
tar xzf ~/pockecom/download/emulator.tar.gz -C ~/pockecom/Emulator --strip-components=1
~/pockecom/Emulator/PC-E500
```

| コマンド | 何をするか |
|---|---|
| `mkdir -p フォルダ` | 置き場所のフォルダを作る |
| `get …` | エミュレータの最新版を取る |
| `tar xzf ファイル -C フォルダ` | `.tar.gz` を展開する。`x` は取り出す、`z` は gzip の圧縮を解く、`f` は次にファイル名を書くという意味。`-C` は取り出す場所、`--strip-components=1` は中にある版付きのフォルダを1段省く |
| `~/pockecom/Emulator/PC-E500` | エミュレータを起動する |

管理者の権限は要りません。足りないライブラリがあるときは [20章](20_TROUBLESHOOTING.md)を見てください。データも、このフォルダの中に置きます（zip の Windows 版と同じ）。

どこからでも `pokecom` と打てるようにするには、次の2行を打って、一度ログインし直します。

```sh
mkdir -p ~/.local/bin
ln -sf ~/pockecom/Emulator/pokecom ~/.local/bin/pokecom
```

| コマンド | 何をするか |
|---|---|
| `mkdir -p ~/.local/bin` | 自分用のコマンドを置くフォルダを作る。Ubuntu では、次にログインしたときから PATH（コマンドを探すフォルダの一覧）に入る |
| `ln -sf 元 先` | 元のファイルを指す「リンク」を置く。`-f` は前のリンクがあれば置き換える |

## ROM を置く

ROM は含まれていません。[実機から吸い出した ROM](04_ROM.md) を、データの `rom` フォルダに置いて起動します。自分の ROM は、`~/PC-E500(7.2).ROM`（ホーム）にある例で書きます。

`.deb` で入れた場合：

```sh
mkdir -p ~/.local/share/PockeComE500/rom
cp ~/'PC-E500(7.2).ROM' ~/.local/share/PockeComE500/rom/
```

tar.gz の場合：

```sh
cp ~/'PC-E500(7.2).ROM' ~/pockecom/Emulator/rom/
```

`cp 元 先` は、ファイルをコピーします。名前の `( )` はシェルの記号なので `'…'` で囲みます。

右クリックのメニューの「データフォルダを開く」でも開けます。ほかの場所の ROM は「ROMを開く」で選びます。

## Windows 版との違い

操作は [5章](05_FIRST_STEPS.md)からの説明のとおりです。違うのは次の点だけです。

| 項目 | Ubuntu |
|---|---|
| ファイルを選ぶ画面 | デスクトップの標準のもの（GNOME ならファイル選択の画面）が開きます |
| 「プログラム読込」の実行 | 選んだあとで「読み込んだあと、実行しますか」と聞きます（Windows では、選ぶウィンドウのチェックで決めます）。ドラッグ＆ドロップのときは、最後に答えたとおりにします |
| 音 | ALSA の既定の出力に出します（Ubuntu では PipeWire につながります） |
| CE-126P の印刷先 | USB のサーマルプリンタ（`/dev/usb/lp0` など）と、CUPS に登録したプリンタ（[12章](12_CE-126P.md)） |
| コントローラー | カーネルのジョイスティック（`/dev/input/js0` など）を読みます（[13章](13_CONTROLLER.md)） |
| テーマ | 「システムに合わせる」は、デスクトップのライト・ダークに合わせます |
| 液晶の標準の表示 | ドットの縁を、画面のピクセルに合わせてなめらかに描きます |

「本体だけ」「画面だけ」の表示、ドラッグでの移動、絵の縁での拡大・縮小、「常に手前に表示」も同じように使えます。

![本体だけの表示](../images/linux-body.png)

## 更新とアンインストール

新しい版が出ると、ツールバーに「新しい版」のボタンが出ます。エミュレータを終了してから、[入れかた](#入れかた)の `get` からもう一度打ちます。`.deb` は新しい版に入れ替わり、tar.gz は同じフォルダに上書きされます。ROM・RAM・設定はそのまま残ります。

消すときは、`.deb` なら `sudo apt remove pc-e500-emulator`（データは残ります）、tar.gz なら `~/pockecom/Emulator` と `~/.local/bin/pokecom` を消します（データも消えます）。

## Windows とデータを行き来する

状態ファイル（`.e500state`）、RAM カードのイメージ（`.ram`）、X: のフォルダのファイルは、Windows 版と同じ形式です。コピーすればそのまま使えます。設定（`state/settings.json`）には、ROM や X: のフォルダの場所が入っているので、写したときは「ROMを開く」と「設定」で選び直してください。

BASIC のテキストは、行末が LF だけのファイル（Linux で作ったもの）も読めます。

---

[← 1. Windows で使う](01_WINDOWS.md) ｜ [目次](../README.md) ｜ [3. Mac で使う（コマンドライン） →](03_MACOS.md)
