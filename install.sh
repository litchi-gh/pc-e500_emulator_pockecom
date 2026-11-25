#!/bin/sh
# Installs the PC-E500 emulator and the command pokecom on Linux, and pokecom on macOS,
# in the user's own folders; run again, it brings them up to date. ROMs and data are kept.
# The folder and the options are those of pc-e500_tools' install.sh.
#
#   sh install.sh [--rom FILE] [--deb] [--emulator FILE]
set -eu

HOME_DIR=${POCKECOM_HOME:-$HOME/pockecom}
BIN_DIR=$HOME/.local/bin
REPO=litchi-gh/pc-e500_emulator_pockecom

case "${E500_LANG:-${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}}" in ja*) JA=1 ;; *) JA= ;; esac
say() { if [ -n "$JA" ]; then printf '%s\n' "$1"; else printf '%s\n' "$2"; fi; }
die() { say "$1" "$2" >&2; exit 1; }

usage() {
  say "sh install.sh [--rom ファイル] [--deb] [--emulator ファイル]
  --rom ファイル       自分の ROM を rom フォルダにコピーする
  --deb                .deb で入れる（Ubuntu。管理者のパスワードを聞かれる）
  --emulator ファイル  取ってくる代わりに、手元の .tar.gz（--deb なら .deb）を使う" \
      "sh install.sh [--rom FILE] [--deb] [--emulator FILE]
  --rom FILE       Copy a ROM of yours into the rom folder
  --deb            Install the .deb (Ubuntu; asks for the administrator's password)
  --emulator FILE  Use this .tar.gz (or .deb, with --deb) instead of the latest one"
}

# fetch URL [FILE]: to the file, or to standard output.
fetch() {
  if command -v curl >/dev/null 2>&1; then
    if [ $# -gt 1 ]; then curl -fL --progress-bar -o "$2" "$1"; else curl -fsSL "$1"; fi
  elif command -v wget >/dev/null 2>&1; then
    if [ $# -gt 1 ]; then wget -q -O "$2" "$1"; else wget -q -O - "$1"; fi
  else die "curl か wget が要ります。" "curl or wget is needed."; fi
}

# latest ENDING FILE: the file of the latest release whose name ends so.
latest() {
  if ! list=$(fetch "https://api.github.com/repos/$REPO/releases/latest"); then
    die "リリースを調べられません。ネットワークを確かめて、もう一度実行してください。" "The releases cannot be read. Check the network and run this again."
  fi
  asset=$(printf '%s\n' "$list" | grep -o '"browser_download_url": *"[^"]*"' | cut -d'"' -f4 | grep -- "$1\$" | head -n 1) || asset=
  [ -n "$asset" ] || die "最新のリリースに、名前が $1 で終わるファイルがありません: https://github.com/$REPO/releases" "The latest release has no file that ends in $1: https://github.com/$REPO/releases"
  say "取得: $asset" "Getting $asset"
  fetch "$asset" "$2"
}

install_deb() {
  command -v apt-get >/dev/null 2>&1 || die ".deb は Ubuntu 用です。" "The .deb is for Ubuntu."
  if [ -n "$EMULATOR" ]; then cp "$EMULATOR" "$WORK/emulator.deb"; else latest amd64.deb "$WORK/emulator.deb"; fi
  # apt reads the file as another user.
  chmod 755 "$WORK"; chmod 644 "$WORK/emulator.deb"
  say "自分のパスワードを聞かれたら打ちます（表示されません）。" "Type your password if you are asked for it (it is not shown)."
  if [ "$(id -u)" = 0 ]; then apt-get install -y "$WORK/emulator.deb"; else sudo apt-get install -y "$WORK/emulator.deb"; fi
  ROM_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/PockeComE500/rom
  POKECOM=/usr/bin/pokecom
}

install_folder() {
  if [ -n "$EMULATOR" ]; then cp "$EMULATOR" "$WORK/emulator.tar.gz"
  elif [ "$SYSTEM" = linux ]; then latest "linux-$ARCH.tar.gz" "$WORK/emulator.tar.gz"
  else latest "osx-$ARCH.tar.gz" "$WORK/emulator.tar.gz"; fi
  # Over what is there: the ROMs, the settings and the X: drive are not in the archive.
  mkdir -p "$HOME_DIR/Emulator"
  tar -xzf "$WORK/emulator.tar.gz" -C "$HOME_DIR/Emulator" --strip-components=1
  [ -f "$HOME_DIR/Emulator/pokecom" ] || die "pokecom の入っていないファイルです。" "The file has no pokecom."
  if [ "$SYSTEM" = darwin ]; then xattr -dr com.apple.quarantine "$HOME_DIR/Emulator" 2>/dev/null || true; fi
  ROM_DIR=$HOME_DIR/Emulator/rom
  POKECOM=$BIN_DIR/pokecom
  write_command pokecom pokecom
  if [ "$SYSTEM" = linux ]; then write_command pc-e500 PC-E500; write_entry; missing_libraries; fi
  add_path
}

# write_command NAME PROGRAM: a short file in ~/.local/bin that starts the program.
write_command() {
  mkdir -p "$BIN_DIR"
  {
    printf '%s\n' '#!/bin/sh' '# Written by install.sh of pc-e500_emulator_pockecom.'
    printf "exec '%s/Emulator/%s' \"\$@\"\n" "$(printf %s "$HOME_DIR" | sed "s/'/'\\\\''/g")" "$2"
  } > "$BIN_DIR/$1.new"
  chmod +x "$BIN_DIR/$1.new"
  # Another program of that name is kept.
  if [ -e "$BIN_DIR/$1" ] && ! grep -q 'pc-e500_emulator_pockecom' "$BIN_DIR/$1" 2>/dev/null; then mv "$BIN_DIR/$1" "$BIN_DIR/$1.old"; fi
  mv -f "$BIN_DIR/$1.new" "$BIN_DIR/$1"
}

# The app list, unless the .deb has put it there.
write_entry() {
  [ ! -d /opt/pc-e500 ] || return 0
  apps=${XDG_DATA_HOME:-$HOME/.local/share}/applications
  mkdir -p "$apps"
  cat > "$apps/pc-e500.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=PC-E500 Emulator
Comment=SHARP PC-E500 series pocket computer emulator
Comment[ja]=ポケットコンピュータ PC-E500 シリーズのエミュレータ
Exec="$HOME_DIR/Emulator/PC-E500"
Icon=$HOME_DIR/Emulator/pc-e500.png
Terminal=false
Categories=Game;Emulator;
StartupWMClass=PC-E500
EOF
}

# The .tar.gz brings no libraries.
missing_libraries() {
  ldconfig=$(command -v ldconfig || echo /sbin/ldconfig)
  [ -x "$ldconfig" ] || return 0
  found=$("$ldconfig" -p 2>/dev/null) || return 0
  missing=
  for lib in libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1 libasound.so.2 libicuuc.so libssl.so.3; do
    case $found in *"$lib"*) ;; *) missing="$missing $lib" ;; esac
  done
  [ -z "$missing" ] || say "足りないライブラリがあります:$missing
--deb を付けて実行すると、一緒に入ります。" "Libraries are missing:$missing
Run this with --deb and they come with it."
}

# ~/.local/bin on PATH, in the file the shell reads; once.
add_path() {
  case ":$PATH:" in *":$BIN_DIR:"*) return 0 ;; esac
  case "${SHELL:-}" in */zsh) profile=$HOME/.zshrc ;; */bash) profile=$HOME/.bashrc ;; *) profile=$HOME/.profile ;; esac
  # shellcheck disable=SC2016
  path='export PATH="$HOME/.local/bin:$PATH"'
  if ! { [ -f "$profile" ] && grep -qF "$path" "$profile"; }; then printf '\n# pc-e500_emulator_pockecom\n%s\n' "$path" >> "$profile"; fi
  REOPEN=1
}

main() {
  ROM=; EMULATOR=; DEB=; REOPEN=
  while [ $# -gt 0 ]; do
    case $1 in
      --rom|--emulator)
        [ $# -gt 1 ] || die "$1 にファイルの名前がありません。" "$1 needs a file name."
        [ -f "$2" ] || die "ファイルがありません: $2" "There is no file $2"
        case $1 in --rom) ROM=$2 ;; *) EMULATOR=$2 ;; esac
        shift ;;
      --deb) DEB=1 ;;
      -h|--help) usage; exit 0 ;;
      *) usage >&2; exit 2 ;;
    esac
    shift
  done
  case $(uname -s) in Linux) SYSTEM=linux ;; Darwin) SYSTEM=darwin ;; *) die "Linux と macOS 用です。" "This is for Linux and macOS." ;; esac
  case $(uname -m) in x86_64|amd64) ARCH=x64 ;; arm64|aarch64) ARCH=arm64 ;; *) die "対応していない CPU です: $(uname -m)" "This CPU is not supported: $(uname -m)" ;; esac
  [ "$SYSTEM$ARCH" != linuxarm64 ] || die "Linux は x86-64 だけに対応しています。" "On Linux only x86-64 is supported."
  [ "$(id -u)" != 0 ] || [ -n "${E500_AS_ROOT:-}" ] || die "sudo を付けずに実行してください（自分のフォルダに入れます）。" "Run this without sudo: it installs into your own folder."

  WORK=$(mktemp -d "${TMPDIR:-/tmp}/pockecom.XXXXXX")
  trap 'rm -rf "$WORK"' EXIT
  trap 'exit 130' INT TERM

  if [ -n "$DEB" ]; then install_deb; else install_folder; fi
  mkdir -p "$ROM_DIR"
  if [ -n "$ROM" ]; then cp "$ROM" "$ROM_DIR/"; fi

  say "" ""
  version=$("$POKECOM" version) || die "pokecom が動きません。上のメッセージを見てください。" "pokecom does not run. See the message above."
  printf 'pokecom %s\n' "$version"
  if ls "$ROM_DIR/"*.[Rr][Oo][Mm] >/dev/null 2>&1; then "$POKECOM" roms
  else say "自分の ROM を $ROM_DIR に置いてください。" "Put your ROM into $ROM_DIR."; fi
  if [ "$SYSTEM" = linux ]; then names="pc-e500、pokecom"; namesen="pc-e500 and pokecom"; else names=pokecom; namesen=pokecom; fi
  if [ -n "$REOPEN" ]; then say "新しい端末（ターミナル）を開くと、$names が使えます。" "Open a new terminal to use $namesen."
  else say "入りました。$names が使えます。" "Done: $namesen can be used."; fi
}

# At the end, so that nothing runs when only a part of the script has come.
main "$@"
