// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>LCD の残像（ドットの濃さ）と、動くドットの補完。</summary>
internal sealed class LcdMotion
{
    // The afterimage (frame interpolation): every dot has a darkness of its own
    // that moves towards on or off over a few frames, as the slow liquid crystal
    // of the real glass does, instead of jumping. A program redraws its screen
    // about ten times a second; the frames between then show the dots on their
    // way, so a moving picture runs on instead of stepping. A dot gets most of the
    // way on in RiseSeconds and off in FallSeconds (the real glass is slower to
    // let go), short enough that text stays sharp to read. 25 ms and 50 ms left a
    // dot going off visible for four frames, a trail behind anything that moved;
    // now it is gone in about two, enough to hide a picture caught half redrawn.
    // 残像（フレーム補完）：ドットごとに濃さを持ち、点く・消えるを数コマかけて変える（実機の液晶の遅い反応と同じ）。
    // 以前の 25ms・50ms では消えるドットが4コマほど残り、動くものが尾を引いたので、2コマほどで消えるようにした。
    const double RiseSeconds=0.010,FallSeconds=0.020;
    bool afterimage;
    /// <summary>ドットの濃さ（0〜1）と、それを 0〜255 にしたもの（描くのはこちら）。</summary>
    readonly float[] level=new float[240*32];
    readonly byte[] shade=new byte[240*32];
    double levelAt=-1;
    /// <summary>ドットの濃さ（描く値。0〜255）。</summary>
    internal byte[] Shade=>shade;
    /// <summary>残像を使うか。</summary>
    internal bool Afterimage=>afterimage;
    /// <summary>残像を切り替える。切り替えたら、今の絵から始め直す。</summary>
    internal void SetAfterimage(bool on){if(afterimage==on)return;afterimage=on;levelAt=-1;}
    /// <summary>ドットの濃さを、LCD の今のドットに向けて now までの時間だけ進める（残像を使わなければ、そのまま写す）。</summary>
    /// <returns>まだ変わっている途中のドットがあるか</returns>
    internal bool Settle(double now,byte[] pixels) {
        if(!afterimage || levelAt<0 || now<levelAt) {
            for(int i=0;i<pixels.Length;i++){bool on=pixels[i]!=0;level[i]=on?1:0;shade[i]=on?(byte)255:(byte)0;}
            levelAt=now;ResetMotion(pixels);return false;
        }
        double dt=now-levelAt;levelAt=now;
        float rise=(float)(1-Math.Exp(-dt/RiseSeconds)),fall=(float)(1-Math.Exp(-dt/FallSeconds));
        bool settling=Slide(now,pixels);
        for(int i=0;i<pixels.Length;i++) {
            float target=pixels[i]!=0?1:0,l=level[i];
            // A dot that moved, and the one it left, change at once: the slide carries the change.
            // A block caught half redrawn at the start of a step keeps its last picture until the rest comes.
            if(holding[i/240/8*BlockCols+i%240/8]){float was=motionBase[i];level[i]=was;shade[i]=(byte)(was*255);settling=true;continue;}
            if(snap[i]){level[i]=target;shade[i]=(byte)(target*255);continue;}
            if(l==target)continue;
            l+=(target-l)*(target>l?rise:fall);
            // Within a step of the last shade, it is there.
            if(MathF.Abs(target-l)<0.5f/255)l=target;else settling=true;
            level[i]=l;shade[i]=(byte)(l*255+0.5f);
        }
        return settling;
    }
    // Motion: a game moves a picture a dot at a time, a few times a second, and
    // the afterimage alone cannot carry it from one place to the next (a row of
    // every other dot, moved by one, would only fade to grey and back). A dot
    // that moved one up, down, left or right is drawn where it was and slid to
    // where it is over as long as its last step took, arriving as the next step
    // comes, so things moving at different speeds each keep their own. A move is
    // found two ways: an 8x8 block whose new picture is its old one moved a dot
    // (a scrolling band, where every other dot fits either way), and a dot that
    // came on beside one that went off (a star passing letters that stay put).
    // A change after a long still, or not such a move (text, a scroll of a whole
    // line), is shown as before. A redraw caught half done (the rest a frame
    // later) is one step.
    // 動きの補完：ゲームは絵を1ドットずつ、1秒に数回動かす。残像だけでは次の位置までつながらない
    // （1つおきのドットの列を1ドット動かすと、灰色になって戻るだけ）。上下左右に1ドット動いたドットは、
    // 前の位置に描いて、そのドットの前の1歩にかかった時間で新しい位置まで滑らせる（次の1歩が来るころに着く。
    // 速さの違うものもそれぞれの速さになる）。動きは2通りで見つける：8×8 の区画の新しい絵が前の絵を1ドット
    // 動かしたもの（流れる帯。1つおきのドットはどちら向きにも合う）と、消えたドットのとなりに点いたドット
    // （止まった文字の間を流れる星）。長く止まっていたあとの変化や、そうでない変化（文字、1行分のスクロール）は
    // 今までどおり。途中まで描かれ、残りが次のコマに描かれたものは1歩と数える。
    const int BlockCols=30,BlockRows=4,Dots=240*32;
    /// <summary>この時間より近い変化は同じ1歩（描き直しの途中）、この時間より長く止まったあとは動きと見ない。</summary>
    const double JoinSeconds=0.05,MaxStepSeconds=0.6;
    static readonly (sbyte X,sbyte Y)[] Moves=[(-1,0),(1,0),(0,-1),(0,1)];
    readonly byte[] motionPrev=new byte[Dots],motionBase=new byte[Dots];
    readonly double[] blockStart=new double[BlockCols*BlockRows],blockStep=new double[BlockCols*BlockRows];
    readonly sbyte[] lastSx=new sbyte[BlockCols*BlockRows],lastSy=new sbyte[BlockCols*BlockRows];
    /// <summary>ドットが点いた（動いて来た）時刻、消えた時刻、消えたドットが動きの元に使われたときの消えた時刻。</summary>
    readonly double[] dotOn=new double[Dots],dotOff=new double[Dots],usedOff=new double[Dots];
    /// <summary>点いているドットが、どちらから動いて来たか（滑り終わっても残す。次の1歩の向きの手がかり）。</summary>
    readonly sbyte[] cameX=new sbyte[Dots],cameY=new sbyte[Dots];
    /// <summary>滑らせている1歩の始まりと長さ（長さ 0 なら滑っていない）。</summary>
    readonly double[] slideStart=new double[Dots],slideStep=new double[Dots];
    // A step that comes a little early or late is not a jerk: the time a step takes is reckoned
    // from the last guess and the last step together, and what was still to go when the next
    // step came is carried into it (up to a dot), so the speed runs on.
    // 1歩が少し早く・遅く来てもガクッとさせない：1歩の時間は前の見込みと今の1歩の平均で見込み、次の1歩が来たときに
    // 残っていた分（1ドットまで）を次の1歩に足して、同じ速さで滑り続ける。
    /// <summary>ドットの1歩の見込みの時間と、滑らせる長さ（ドット。1＋持ち越し）。</summary>
    readonly double[] dotPace=new double[Dots];
    readonly float[] slideDist=new float[Dots],remain=new float[Dots];
    readonly double[] blockPace=new double[BlockCols*BlockRows];
    /// <summary>このコマで変わった・動いた・ドットの組が見つかった区画（Slide の中で使う）。</summary>
    readonly bool[] blockChanged=new bool[BlockCols*BlockRows],blockMoved=new bool[BlockCols*BlockRows],blockPaired=new bool[BlockCols*BlockRows];
    /// <summary>このコマで、濃さを残像なしで今のドットにするか。</summary>
    readonly bool[] snap=new bool[Dots];
    // A game redraws a moved thing over a frame or two; caught half done, the block is no move yet
    // and its new dots would show at once, then slide back from where they were once the rest came.
    // So a block that steps in rhythm but is not (yet) a move keeps its last picture until the step
    // is a move or JoinSeconds have gone.
    // ゲームは動かしたものを1〜2コマかけて描く。途中のコマでは区画が動きと認められず、新しいドットがすぐ出て、残りが
    // 描かれると前の位置から滑り直していた。リズムよく変わる区画が動きと認められないうちは、動きになるか JoinSeconds が
    // 過ぎるまで前の絵のままにする。
    /// <summary>区画を前の絵のままにしているか。</summary>
    readonly bool[] holding=new bool[BlockCols*BlockRows];
    /// <summary>滑っているドットと、描く位置のずれ（ドット）。</summary>
    readonly List<(int Index,float X,float Y)> slides=[];
    /// <summary>ずれが前の呼び出しから変わったか（なら描き直す）。</summary>
    bool offsetsChanged;
    /// <summary>滑っているドットと、描く位置のずれ（ドット）。</summary>
    internal List<(int Index,float X,float Y)> Slides=>slides;
    /// <summary>ずれが前の呼び出しから変わったか（なら描き直す）。</summary>
    internal bool OffsetsChanged=>offsetsChanged;
    /// <summary>今の絵から、動きなしで始め直す。</summary>
    void ResetMotion(byte[] pixels) {
        for(int i=0;i<pixels.Length;i++)motionPrev[i]=pixels[i]!=0?(byte)1:(byte)0;
        offsetsChanged=slides.Count>0;slides.Clear();
        Array.Fill(blockStart,double.MinValue);Array.Clear(blockStep);Array.Clear(blockPace);Array.Clear(dotPace);Array.Clear(remain);
        Array.Fill(dotOn,double.MinValue);Array.Fill(dotOff,double.MinValue);Array.Fill(usedOff,double.NaN);
        Array.Clear(cameX);Array.Clear(cameY);Array.Clear(slideStep);Array.Clear(snap);Array.Clear(holding);
    }
    /// <summary>ドット i を、向き (x, y) の1歩として start から step 秒かけて滑らせる。</summary>
    /// <param name="from">前にいたドット（その残りを持ち越す。なければ -1）</param>
    void StartSlide(int i,sbyte x,sbyte y,double start,double step,int from) {
        float carry=from>=0 && remainX[from]==x && remainY[from]==y?remain[from]:0;
        cameX[i]=x;cameY[i]=y;slideStart[i]=start;slideStep[i]=step;dotPace[i]=step;slideDist[i]=1+Math.Min(1,carry);dotOn[i]=start;snap[i]=true;
    }
    /// <summary>新しい1歩の前に、まだ滑っていたドットの残り（ドット）と向き。</summary>
    readonly sbyte[] remainX=new sbyte[Dots],remainY=new sbyte[Dots];
    /// <summary>前の見込みと今の1歩から、次の1歩の時間を見込む（大きく違えば今の1歩）。</summary>
    static double Pace(double guess,double gap)=>guess>0 && gap>guess*0.6 && gap<guess*1.6?(guess+gap)/2:gap;
    /// <summary>新しい1歩を見つけ、滑らせているドットのずれを now まで進める。</summary>
    /// <returns>まだ滑っているドットがあるか</returns>
    bool Slide(double now,byte[] pixels) {
        Array.Clear(snap);
        bool[] changed=blockChanged,moved=blockMoved,paired=blockPaired;Array.Clear(changed);Array.Clear(moved);Array.Clear(paired);bool any=false;
        for(int b=0;b<changed.Length;b++) {
            int x0=b%BlockCols*8,y0=b/BlockCols*8;
            for(int y=y0;y<y0+8 && !changed[b];y++)for(int x=x0;x<x0+8;x++)if((pixels[y*240+x]!=0?1:0)!=motionPrev[y*240+x]){changed[b]=true;break;}
            // A block held for the rest of its step is looked at again, changed or not:
            // a neighbour may have become a move it goes along with.
            // 前の絵のまま待っている区画は、変わっていなくても見直す（となりが動きになり、一緒に動けることがある）。
            if(!changed[b]){if(holding[b] && now-blockStart[b]<JoinSeconds){changed[b]=true;any=true;}continue;}
            any=true;
            // A new step: the picture before it is what the move is found from.
            if(now-blockStart[b]>=JoinSeconds) {
                double step=now-blockStart[b];
                blockStep[b]=step<=MaxStepSeconds?step:0;blockStart[b]=now;blockPace[b]=blockStep[b]>0?Pace(blockPace[b],step):0;
                for(int y=y0;y<y0+8;y++)Array.Copy(motionPrev,y*240+x0,motionBase,y*240+x0,8);
            }
        }
        if(any) {
            // What each sliding dot still had to go, before this step ends the slides.
            // Taken when a block's step starts; the frames that finish a half-drawn step keep it.
            // 区画の1歩が始まるコマで取る（描きかけの1歩を描き終えるコマでは、取ったものを使う）。
            for(int i=0;i<pixels.Length;i++) {
                if(blockStart[i/240/8*BlockCols+i%240/8]!=now)continue;
                remain[i]=0;remainX[i]=cameX[i];remainY[i]=cameY[i];
                if(slideStep[i]>0)remain[i]=slideDist[i]*(float)Math.Clamp(1-(now-slideStart[i])/slideStep[i],0,1);
            }
            for(int i=0;i<pixels.Length;i++) {
                int v=pixels[i]!=0?1:0;if(v==motionPrev[i])continue;
                if(v!=0){dotOn[i]=now;cameX[i]=0;cameY[i]=0;}else dotOff[i]=now;
                slideStep[i]=0;
            }
            // The picture before this step at a dot: the step's start for a block in one, else the last frame.
            int Old(int x,int y) {
                if(x<0 || x>=240 || y<0 || y>=32)return 0;
                int b=y/8*BlockCols+x/8;return now-blockStart[b]<JoinSeconds?motionBase[y*240+x]:motionPrev[y*240+x];
            }
            Span<int> errors=stackalloc int[4];
            for(int b=0;b<changed.Length;b++) {
                if(!changed[b] || blockStep[b]<=0)continue;
                int x0=b%BlockCols*8,y0=b/BlockCols*8,differ=0;errors.Clear();
                for(int y=y0;y<y0+8;y++)for(int x=x0;x<x0+8;x++) {
                    int v=pixels[y*240+x]!=0?1:0;
                    if(v!=motionBase[y*240+x])differ++;
                    for(int m=0;m<4;m++)if(v!=Old(x-Moves[m].X,y-Moves[m].Y))errors[m]++;
                }
                // Most of the change must be the move. A row of every other dot fits a move either way: the block's last direction wins a tie.
                int best=-1;
                for(int m=0;m<4;m++)if(best<0 || errors[m]<errors[best] || errors[m]==errors[best] && Moves[m]==(lastSx[b],lastSy[b]))best=m;
                if(differ<2 || errors[best]*4>differ)continue;
                MoveBlock(b,Moves[best].X,Moves[best].Y,blockStart[b],blockPace[b]);
            }
            // A block the moving thing has only just reached was still before, so it has no
            // step of its own yet: it moves with a neighbour that moved this way, if that
            // move is what changed in it (else the thing's front would stand while the rest slid).
            // 動くものが入ったばかりの区画は、それまで止まっていたので自分の1歩がない。となりの区画が動いた向きで
            // 変化が説明できれば、となりと一緒に滑らせる（そうしないと、先頭だけが止まって残りが滑る）。
            for(int b=0;b<changed.Length;b++) {
                if(!changed[b] || moved[b])continue;
                int x0=b%BlockCols*8,y0=b/BlockCols*8,col=b%BlockCols;
                foreach(int nb in (int[])[col>0?b-1:-1,col<BlockCols-1?b+1:-1,b-BlockCols,b+BlockCols]) {
                    if(nb<0 || nb>=changed.Length || !moved[nb] || nb==b)continue;
                    sbyte sx=lastSx[nb],sy=lastSy[nb];int differ=0,wrong=0;
                    for(int y=y0;y<y0+8;y++)for(int x=x0;x<x0+8;x++) {
                        int v=pixels[y*240+x]!=0?1:0;
                        if(v!=motionBase[y*240+x])differ++;
                        if(v!=Old(x-sx,y-sy))wrong++;
                    }
                    if(differ<1 || wrong*4>differ)continue;
                    MoveBlock(b,sx,sy,blockStart[nb],blockPace[nb]);break;
                }
            }
            /// <summary>区画 b の1歩を、向き (sx, sy) の動きとして滑らせる。</summary>
            void MoveBlock(int b,sbyte sx,sbyte sy,double start,double pace) {
                int x0=b%BlockCols*8,y0=b/BlockCols*8;
                lastSx[b]=sx;lastSy[b]=sy;moved[b]=true;
                for(int y=y0;y<y0+8;y++)for(int x=x0;x<x0+8;x++) {
                    int i=y*240+x;snap[i]=true;
                    // A dot that came along with the move slides; so does one that is new in
                    // this step (the band's newest dot, entering at its end), so it does not
                    // stand at its place beside its neighbour still on the way. A dot that
                    // stays (lit before and now, with nothing behind it) does not move.
                    // 動きと一緒に来たドットは滑らせる。この1歩で新しく点いたドット（帯の端に入ってくる新しいドット）も
                    // 同じく滑らせる（途中のとなりのドットの横に、先に着いて並ばないように）。前から点いていて、
                    // 後ろに何もないドット（止まったもの）は動かさない。
                    if(pixels[i]==0 || Old(x-sx,y-sy)==0 && motionBase[i]!=0)continue;
                    int behind=x-sx>=0 && x-sx<240 && y-sy>=0 && y-sy<32?(y-sy)*240+x-sx:-1;
                    StartSlide(i,sx,sy,start,pace,behind);
                    int qx=x-sx,qy=y-sy;
                    if(qx>=0 && qx<240 && qy>=0 && qy<32 && pixels[qy*240+qx]==0)usedOff[qy*240+qx]=dotOff[qy*240+qx];
                }
            }
            // Single dots: one that came on lately beside one that went off lately, not yet taken.
            for(int i=0;i<pixels.Length;i++) {
                if(pixels[i]==0 || now-dotOn[i]>=JoinSeconds || slideStep[i]>0 || cameX[i]!=0 || cameY[i]!=0)continue;
                int x=i%240,y=i/240;
                if(moved[y/8*BlockCols+x/8])continue;
                int found=-1,count=0;double start=0,step=0;bool agrees=false;
                for(int m=0;m<4;m++) {
                    int qx=x-Moves[m].X,qy=y-Moves[m].Y;
                    if(qx<0 || qx>=240 || qy<0 || qy>=32)continue;
                    int q=qy*240+qx;
                    if(pixels[q]!=0 || now-dotOff[q]>=JoinSeconds || usedOff[q]==dotOff[q])continue;
                    // The step is from when the dot it left got there.
                    double s=Math.Min(dotOn[i],dotOff[q]),gap=s-dotOn[q];
                    if(gap<JoinSeconds || gap>MaxStepSeconds)continue;
                    count++;
                    // Two ways to have come: the way that dot came before.
                    bool same=Moves[m]==(cameX[q],cameY[q]);
                    if(found<0 || same){found=m;start=s;step=Pace(dotPace[q],gap);agrees=same;}
                }
                if(found<0 || count>1 && !agrees)continue;
                int from=(y-Moves[found].Y)*240+x-Moves[found].X;
                StartSlide(i,Moves[found].X,Moves[found].Y,start,step,from);usedOff[from]=dotOff[from];snap[from]=true;paired[y/8*BlockCols+x/8]=true;
            }
            for(int b=0;b<changed.Length;b++)if(changed[b] && !moved[b] && !paired[b] && blockStep[b]>0 && now-blockStart[b]<JoinSeconds)holding[b]=true;
            // A block still before, beside one held or moving (a moving thing's front reaching
            // it in a half-drawn frame), waits with it rather than showing its part at once.
            // 止まっていた区画も、となりが待っているか動いているなら一緒に待つ（描きかけのコマで、動くものの先頭が入ってきたとき）。
            for(int b=0;b<changed.Length;b++) {
                if(!changed[b] || moved[b] || paired[b] || holding[b] || now-blockStart[b]>=JoinSeconds)continue;
                int col=b%BlockCols;
                foreach(int nb in (int[])[col>0?b-1:-1,col<BlockCols-1?b+1:-1,b-BlockCols,b+BlockCols])
                    if(nb>=0 && nb<holding.Length && (holding[nb] || moved[nb])){holding[b]=true;break;}
            }
            for(int i=0;i<pixels.Length;i++)motionPrev[i]=pixels[i]!=0?(byte)1:(byte)0;
        }
        for(int b=0;b<holding.Length;b++)if(holding[b] && (moved[b] || paired[b] || now-blockStart[b]>=JoinSeconds))holding[b]=false;
        bool was=slides.Count>0;slides.Clear();
        for(int i=0;i<pixels.Length;i++) {
            if(slideStep[i]<=0)continue;
            double f=(now-slideStart[i])/slideStep[i];
            if(pixels[i]==0 || f>=1){slideStep[i]=0;continue;}
            float left=(float)(1-Math.Max(0,f));
            slides.Add((i,-cameX[i]*left*slideDist[i],-cameY[i]*left*slideDist[i]));snap[i]=true;
        }
        offsetsChanged=was || slides.Count>0;
        return slides.Count>0;
    }
}
