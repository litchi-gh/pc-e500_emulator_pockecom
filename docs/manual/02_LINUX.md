# 2. Ubuntu（Linux）で使う

Ubuntu のデスクトップで、Windows 版と同じ操作のエミュレータが動きます。ROM の処理も、保存するファイルの形式も同じです。

![Ubuntu で動かしたところ](../images/linux-main.png)

## 動作環境

| 項目 | 内容 |
|---|---|
| OS | Ubuntu 24.04 以降（x86-64）。デスクトップは Wayland でも X11 でもかまいません（Wayland では XWayland を通して動きます） |
| 入れておくもの | .NET のランタイムは同梱しています。`--deb` を付けて入れると、足りないライブラリ（X11・フォント・ALSA など）も一緒に入ります |
| フォント | 日本語の表示に Noto Sans CJK を使います（`fonts-noto-cjk`。Ubuntu の日本語の環境には入っています） |
| ディスク | 約 180MB |

## 入れかた

端末を開いて、次の1行を貼り付けます。いちばん新しいバージョンを GitHub から取って、入れ終わりまで進みます。新しくするときも同じ行をもう一度実行します（ROM・RAM・設定はそのまま残ります）。

```sh
wget -qO- https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh | sh
```

`wget -qO- アドレス` が、インストール用のスクリプト `install.sh` を GitHub から取り、`| sh` がそれを実行します。オプションは `| sh -s --` の後ろに書きます。自分の ROM（例 `~/PC-E500(7.2).ROM`）も一緒に置くなら、次のようにします。

```sh
wget -qO- https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh | sh -s -- --rom ~/'PC-E500(7.2).ROM'
```

中身を見てから実行したいときは、次のように分けます。

```sh
wget -qO- https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh > install.sh
less install.sh
sh install.sh
```

| コマンド | 何をするか |
|---|---|
| `wget -qO- アドレス > install.sh` | スクリプトを取って、いまいるフォルダに保存する |
| `less install.sh` | 中身を見る（`q` で終わる） |
| `sh install.sh` | 実行する |

| オプション | 意味 |
|---|---|
| `--rom ファイル` | 自分の ROM を `rom` フォルダにコピーする。名前の `( )` はシェルの記号なので `'…'` で囲む |
| `--deb` | `.deb` で入れる（管理者のパスワードを聞かれる） |
| `--emulator ファイル` | 取ってくる代わりに、手元の `.tar.gz`（`--deb` を付けたときは `.deb`）を使う |

### 入るもの

| もの | `--deb` なし | `--deb` あり |
|---|---|---|
| プログラム | `~/pockecom/Emulator` | `/opt/pc-e500` |
| 端末のコマンド（`pc-e500`・[`pokecom`](16_CLI.md)） | `~/.local/bin` | `/usr/bin` |
| ROM・RAM カード・X: のフォルダ・設定 | `~/pockecom/Emulator` の中 | `~/.local/share/PockeComE500`（利用者ごと） |
| アプリの一覧 | 「PC-E500 Emulator」 | 「PC-E500 Emulator」 |
| 管理者の権限 | 要らない | 要る |
| 足りないライブラリ（X11・フォント・ALSA など） | 入らない（足りなければ、終わりに名前が出る） | 一緒に入る |

- `sudo` を付けずに実行します。`--deb` のときだけ、スクリプトが `sudo apt-get` を使います。
- `~/.local/bin` が PATH（コマンドを探すフォルダの一覧）になければ、スクリプトが `~/.bashrc` に1行足します。**新しい端末を開くと**、`pc-e500` と `pokecom` が打てるようになります。
- `~/pockecom` を別の場所にするには、`POCKECOM_HOME=フォルダ sh install.sh` のように書きます。
- 置き場所は[PC-E500 開発ツール](https://github.com/litchi-gh/pc-e500_tools)の `install.sh` と同じです。そちらで入れたエミュレータはそのまま使えます。

入ったかどうかを確かめます。

```sh
pokecom version
pokecom roms
pc-e500
```

| コマンド | 何をするか | 正しいとき |
|---|---|---|
| `pokecom version` | バージョンを表示する | `0.2.1` など |
| `pokecom roms` | 見つかった ROM を並べる | 自分の ROM が出る |
| `pc-e500` | エミュレータを起動する | ウィンドウが開く |

## ROM を置く

ROM は含まれていません。[実機から吸い出した ROM](04_ROM.md) を `rom` フォルダに置いて起動します。`--rom` を付けて入れたときは、もう置いてあります。あとから置くときは、次のようにします。

```sh
cp ~/'PC-E500(7.2).ROM' ~/pockecom/Emulator/rom/
```

`--deb` で入れたときは、こちらです。

```sh
cp ~/'PC-E500(7.2).ROM' ~/.local/share/PockeComE500/rom/
```

`cp 元 先` はファイルをコピーします。

`rom` フォルダのあるデータフォルダは、右クリックのメニューの「データフォルダを開く」でも開けます。ほかの場所の ROM は「ROMを開く」で選びます。

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
| 液晶の標準の表示 | ドットの縁を画面のピクセルに合わせてなめらかに描きます |

「本体だけ」「画面だけ」の表示、ドラッグでの移動、絵の縁での拡大・縮小、「常に手前に表示」も同じように使えます。

![本体だけの表示](../images/linux-body.png)

## 更新とアンインストール

新しいバージョンが出ると、ツールバーに「新しいバージョン」のボタンが出ます。エミュレータを終了してから、[入れかた](#入れかた)の1行をもう一度実行します（`--deb` で入れたときは `--deb` を付けます）。ROM・RAM・設定はそのまま残ります。

消すときは、次のようにします。`--deb` なしで入れたときはデータも消えるので、ROM などを先に移してから実行してください。

```sh
rm -r ~/pockecom/Emulator
rm ~/.local/bin/pc-e500 ~/.local/bin/pokecom ~/.local/share/applications/pc-e500.desktop
```

`--deb` で入れたときは、こちらです。データは残ります。

```sh
sudo apt remove pc-e500-emulator
```

| コマンド | 何をするか |
|---|---|
| `rm -r フォルダ` | フォルダを、中身ごと消す |
| `rm ファイル…` | ファイルを消す。ここでは端末のコマンドと、アプリの一覧の項目 |
| `sudo apt remove 名前` | パッケージを外す |

## Windows とデータを行き来する

状態ファイル（`.e500state`）、RAM カードのイメージ（`.ram`）、X: のフォルダのファイルは、Windows 版と同じ形式です。コピーすればそのまま使えます。設定（`state/settings.json`）には ROM や X: のフォルダの場所が入っているので、写したときは「ROMを開く」と「設定」で選び直してください。

BASIC のテキストは、行末が LF だけのファイル（Linux で作ったもの）も読めます。

---

[← 1. Windows で使う](01_WINDOWS.md) ｜ [目次](../README.md) ｜ [3. Mac で使う（コマンドライン） →](03_MACOS.md)
