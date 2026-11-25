// Build (Git Bash):
//   MSYS_NO_PATHCONV=1 /c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe /nologo /codepage:65001
//     /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
//     "/out:최소성취수준 도우미.exe" App.cs
// Check:  "최소성취수준 도우미.exe" --selftest result.txt   (exit code 0 = all passed)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.IO;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// ───────────── 자료 (이름 칸은 일부러 없음: 반·번호만) ─────────────
public class Item { public string Name = ""; public string Kind = "수행평가"; public double Max = 100; public double Weight = 0; }
public class Row { public int Ban; public int Num; public List<double?> Scores = new List<double?>(); public int Absent; }
public class Subject
{
    public string Name = ""; public bool UseAchv = true; public int TotalLessons;
    public List<Item> Items = new List<Item>(); public List<Row> Rows = new List<Row>();
}
public class Data { public double Cut = 40; public double Margin = 10; public List<Subject> Subjects = new List<Subject>(); }

public class Res
{
    public double Earned, EnteredW; public double? Cur, Need;
    public int Achv, Att, Level, Allowed;
    public string AchvText = "", AttText = "", Reason = "", Label = "-";
}

// ───────────── 판정 ─────────────
public static class Calc
{
    public static readonly string[] Names = { "-", "안전", "주의", "위험", "미도달" };
    const double Eps = 1e-9;
    public static string F(double v) { return v.ToString("0.#"); }

    public static Res Judge(Data d, Subject s, Row r)
    {
        Res x = new Res();
        // 학업성취율: 본 평가만으로 지금까지 얻은 점수(100점 환산)와, 남은 평가에서 필요한 득점률
        for (int i = 0; i < s.Items.Count; i++)
        {
            Item it = s.Items[i];
            double? sc = i < r.Scores.Count ? r.Scores[i] : null;
            if (sc.HasValue && it.Max > 0) { x.Earned += sc.Value / it.Max * it.Weight; x.EnteredW += it.Weight; }
        }
        if (x.EnteredW > Eps)
        {
            double remain = Math.Max(0, 100 - x.EnteredW);   // 아직 등록 안 한 평가도 '남은 평가'로 본다
            x.Cur = x.Earned / x.EnteredW * 100;
            string cut = F(d.Cut);
            if (x.Earned >= d.Cut - Eps) { x.Achv = 1; }
            else if (remain <= Eps) { x.Achv = 4; x.AchvText = "최종 " + F(x.Earned) + "점 - " + cut + "점 미달"; }
            else
            {
                x.Need = (d.Cut - x.Earned) / remain * 100;
                if (x.Need > 100 + Eps) { x.Achv = 4; x.AchvText = "지금 " + F(x.Earned) + "점. 남은 평가를 모두 만점 받아도 " + cut + "점이 안 됨"; }
                else if (x.Cur < d.Cut - Eps) { x.Achv = 3; x.AchvText = "지금까지 득점률 " + F(x.Cur.Value) + "% (기준 " + cut + "%). 남은 평가에서 " + F(x.Need.Value) + "% 이상 받아야 함"; }
                else if (x.Cur < d.Cut + d.Margin - Eps) { x.Achv = 2; x.AchvText = "지금까지 득점률 " + F(x.Cur.Value) + "% - 기준에 가까움. 남은 평가에서 " + F(x.Need.Value) + "% 이상 필요"; }
                else x.Achv = 1;
            }
        }
        // 출석률 2/3 이상  <=>  결석 <= 전체 수업 횟수 / 3 (내림)
        if (s.TotalLessons > 0)
        {
            x.Allowed = s.TotalLessons / 3;
            string t = "결석 " + r.Absent + "회 (허용 " + x.Allowed + "회)";
            if (r.Absent > x.Allowed) { x.Att = 4; x.AttText = t + " - 출석률 2/3 미달"; }
            else
            {
                x.Att = r.Absent * 10 >= x.Allowed * 7 && r.Absent > 0 ? 3 : r.Absent * 2 >= x.Allowed && r.Absent > 0 ? 2 : 1;
                x.AttText = t + " - 앞으로 " + (x.Allowed - r.Absent + 1) + "번 더 결석하면 미달";
            }
        }
        // 종합: 선택과목은 출석만 판정하고 성취율은 참고로만 적는다
        x.Level = Math.Max(s.UseAchv ? x.Achv : 0, x.Att);
        List<string> why = new List<string>();
        if (s.UseAchv && x.Achv >= 2) why.Add(x.AchvText);
        if (x.Att >= 2) why.Add(x.AttText);
        if (!s.UseAchv && x.Achv >= 3) why.Add("(참고) " + x.AchvText);
        x.Reason = string.Join(" / ", why.ToArray());
        x.Label = (s.UseAchv && x.Achv == 0 && x.Level <= 1) ? "점수 없음" : Names[x.Level];
        return x;
    }
}

// ───────────── 저장 ─────────────
public static class Store
{
    public static Data Parse(string json)
    {
        Data d = new JavaScriptSerializer().Deserialize<Data>(json);
        if (d == null) d = new Data();
        if (d.Subjects == null) d.Subjects = new List<Subject>();
        foreach (Subject s in d.Subjects)
        {
            if (s.Name == null) s.Name = "";
            if (s.Items == null) s.Items = new List<Item>();
            if (s.Rows == null) s.Rows = new List<Row>();
            foreach (Row r in s.Rows)
            {
                if (r.Scores == null) r.Scores = new List<double?>();
                while (r.Scores.Count < s.Items.Count) r.Scores.Add(null);
                if (r.Scores.Count > s.Items.Count) r.Scores.RemoveRange(s.Items.Count, r.Scores.Count - s.Items.Count);
            }
        }
        return d;
    }
}

// 암호로 잠근 자료 파일.
//   파일 = "MINACH01"(8) + salt(16) + iv(16) + AES-256-CBC(자료) + HMAC-SHA256(앞부분 전체)(32)
//   키  = PBKDF2-HMAC-SHA1(암호, salt, 30만 회) 32바이트에서 암호화 키·검증 키를 갈라 씀
// 암호도, 암호의 해시도 어디에도 저장하지 않는다. 맞는 암호인지는 HMAC 이 맞는지로만 안다.
// 그래서 이 소스를 다 읽어도 암호를 알아낼 길은 "하나씩 넣어 보기"뿐이고, 한 번 넣어 보는 데 30만 회 계산이 든다.
public class Vault
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("MINACH01");
    const int Iter = 300000, Head = 8 + 16 + 16, Mac = 32;
    readonly byte[] salt, encKey, macKey;

    Vault(string password, byte[] s)
    {
        salt = s; byte[] master;
        using (Rfc2898DeriveBytes k = new Rfc2898DeriveBytes(password, salt, Iter)) master = k.GetBytes(32);
        using (HMACSHA256 h = new HMACSHA256(master)) { encKey = h.ComputeHash(Encoding.ASCII.GetBytes("enc")); macKey = h.ComputeHash(Encoding.ASCII.GetBytes("mac")); }
    }
    static byte[] Rand(int n) { byte[] b = new byte[n]; using (RNGCryptoServiceProvider r = new RNGCryptoServiceProvider()) r.GetBytes(b); return b; }
    public static Vault Create(string password) { return new Vault(password, Rand(16)); }

    // 암호가 틀렸거나 파일이 손상됐으면 null (둘은 구별할 수 없다)
    public static Vault Open(string path, string password, out Data d)
    {
        d = null; byte[] f = File.ReadAllBytes(path);
        if (f.Length < Head + 16 + Mac) return null;
        for (int i = 0; i < 8; i++) if (f[i] != Magic[i]) return null;
        byte[] s = new byte[16], iv = new byte[16]; Array.Copy(f, 8, s, 0, 16); Array.Copy(f, 24, iv, 0, 16);
        Vault v = new Vault(password, s); int diff = 0;
        using (HMACSHA256 h = new HMACSHA256(v.macKey)) { byte[] m = h.ComputeHash(f, 0, f.Length - Mac); for (int i = 0; i < Mac; i++) diff |= m[i] ^ f[f.Length - Mac + i]; }
        if (diff != 0) return null;
        using (Aes a = Aes.Create())
        using (ICryptoTransform t = a.CreateDecryptor(v.encKey, iv))
            d = Store.Parse(Encoding.UTF8.GetString(t.TransformFinalBlock(f, Head, f.Length - Head - Mac)));
        return v;
    }

    public void Save(string path, Data d)
    {
        byte[] plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(d)), iv = Rand(16), body;
        using (Aes a = Aes.Create()) using (ICryptoTransform t = a.CreateEncryptor(encKey, iv)) body = t.TransformFinalBlock(plain, 0, plain.Length);
        byte[] f = new byte[Head + body.Length + Mac];
        Array.Copy(Magic, 0, f, 0, 8); Array.Copy(salt, 0, f, 8, 16); Array.Copy(iv, 0, f, 24, 16); Array.Copy(body, 0, f, Head, body.Length);
        using (HMACSHA256 h = new HMACSHA256(macKey)) Array.Copy(h.ComputeHash(f, 0, Head + body.Length), 0, f, Head + body.Length, Mac);
        string tmp = path + ".tmp"; File.WriteAllBytes(tmp, f);
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak"); else File.Move(tmp, path);
    }
}

// ───────────── 자동 업데이트 ─────────────
// 저장소의 dist/version.txt (1줄: 버전, 2줄: 서명) 를 보고, 더 높은 버전이면 dist/app.exe 를 받아 자기 자신과 바꾼다.
// 받은 exe 는 "버전 + exe 전체"에 대한 RSA 서명이 아래 공개키로 맞을 때만 쓴다.
// 서명 키(개인키)는 개발자 PC 에만 있으므로, 저장소가 남의 손에 넘어가도 가짜 exe 는 설치되지 않는다.
public static class Updater
{
    public const string Ver = "1.2";
    public static string Base = "https://raw.githubusercontent.com/avalons87-art/min-achievement-helper/main/dist/";
    public const string PubKey = "<RSAKeyValue><Modulus>wfoSZ+6SXjH8ew6S0+lKzXKvua0nZsXFfhkLGMYCkF0gq7qCdw52YqRpkZwGmGiMfFmc/LmGwU9o0b5q0yKdGmy/Ysa6Rrn1g7XT1m+d+dgq7HDll/eObIX9WDgzSt9JfMdXi/vYdQbmzxLziUGsPBiE3RcJ+vii5hwpvVEMVXItm5iGCOUEznMxW3nddujBnTmu1T80nYvF6L3X0V8Hyro0b6t0skqEjKnvSdxpuVr3r0zq23GGU//yap0qa2hTcrR45PE4Z6BftrZ8SrPulY9Utt1s2pKFTboA8H0Y7zjEzk9oiNcsRjrYZTK0E69JeGIJCBVJve2rtll3A1toXObtmNOkxTOc9NlWJjEPGvEG1QjDfAktwt8c7XiBxuju2LYZrTNaS2EkdAsJoREaJ9Bb2bueqtepsy+udtOMZ4oA+uMmUmnEJDSlV7Q52CAG7I1q5q47YHhakVGPW8FisHGkMtpvyHSzL7vWHmuxjrCi296iiNh3TQmEHI/EKZvh</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    public class Info { public string Version; public byte[] Sig; }

    static byte[] Get(string name)
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
        using (WebClient w = new WebClient()) return w.DownloadData(Base + name + "?t=" + DateTime.UtcNow.Ticks);
    }
    // 새 버전이 없거나 확인하지 못하면(인터넷 없음 등) null - 조용히 넘어간다
    public static Info Check()
    {
        try
        {
            string[] l = Encoding.UTF8.GetString(Get("version.txt")).Trim((char)0xFEFF, ' ', '\r', '\n').Split('\n');
            if (l.Length < 2) return null;
            Info i = new Info(); i.Version = l[0].Trim(); i.Sig = Convert.FromBase64String(l[1].Trim());
            return new System.Version(i.Version).CompareTo(new System.Version(Ver)) > 0 ? i : null;
        }
        catch { return null; }
    }
    static byte[] Signed(string version, byte[] exe)
    {
        byte[] head = Encoding.UTF8.GetBytes(version + "\n"), all = new byte[head.Length + exe.Length];
        Array.Copy(head, all, head.Length); Array.Copy(exe, 0, all, head.Length, exe.Length); return all;
    }
    public static bool Verify(string pubKey, string version, byte[] exe, byte[] sig)
    {
        try { using (RSACryptoServiceProvider r = new RSACryptoServiceProvider()) { r.FromXmlString(pubKey); return r.VerifyData(Signed(version, exe), new SHA256CryptoServiceProvider(), sig); } }
        catch { return false; }
    }
    public static byte[] Sign(string privKey, string version, byte[] exe)
    {
        using (RSACryptoServiceProvider r = new RSACryptoServiceProvider()) { r.FromXmlString(privKey); return r.SignData(Signed(version, exe), new SHA256CryptoServiceProvider()); }
    }
    // 성공하면 null, 아니면 사용자에게 보여 줄 이유. 실패하면 지금 exe 는 그대로다.
    public static string Apply(Info i)
    {
        try
        {
            byte[] exe = Get("app.exe");
            if (!Verify(PubKey, i.Version, exe, i.Sig)) return "내려받은 파일의 서명이 맞지 않아 업데이트하지 않았습니다.\n지금 버전은 그대로 쓸 수 있습니다.";
            string me = Application.ExecutablePath, old = me + ".old", neu = me + ".new";
            File.WriteAllBytes(neu, exe);
            if (File.Exists(old)) File.Delete(old);
            File.Move(me, old);                                    // 실행 중인 exe 도 이름은 바꿀 수 있다
            try { File.Move(neu, me); } catch { File.Move(old, me); throw; }
            return null;
        }
        catch (Exception e) { return "업데이트하지 못했습니다. 지금 버전은 그대로 쓸 수 있습니다.\n\n" + e.Message; }
    }
    // 개발자용: 서명 키 만들기(있으면 덮어쓰지 않음), 배포 파일 만들기
    public static int KeyGen(string keyFile)
    {
        if (File.Exists(keyFile)) return 2;
        using (RSACryptoServiceProvider r = new RSACryptoServiceProvider(3072)) { File.WriteAllText(keyFile, r.ToXmlString(true)); File.WriteAllText(keyFile + ".pub", r.ToXmlString(false)); }
        return 0;
    }
    public static int Release(string keyFile, string outDir)
    {
        byte[] exe = File.ReadAllBytes(Application.ExecutablePath), sig = Sign(File.ReadAllText(keyFile), Ver, exe);
        if (!Verify(PubKey, Ver, exe, sig)) return 3;              // 이 exe 에 박힌 공개키와 짝이 아닌 키로는 내보내지 않는다
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "app.exe"), exe);
        File.WriteAllText(Path.Combine(outDir, "version.txt"), Ver + "\n" + Convert.ToBase64String(sig) + "\n", new UTF8Encoding(false));
        return 0;
    }
}

// ───────────── 시작 화면: 암호 입력 / 처음 사용하기 ─────────────
public static class Login
{
    public const string Chat = "https://open.kakao.com/o/svtrOmLi";
    public const int MinLen = 8;
    public static Updater.Info FakeUpdate;   // 화면 점검용
    public static void OpenChat()
    {
        try { Process.Start(Chat); }
        catch { MessageBox.Show("카카오톡 문의방을 열지 못했습니다. 아래 주소를 인터넷 주소창에 적어 주세요.\n\n" + Chat, "개발자에게 문의하기"); }
    }
    static string[] Existing(string path) { return Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "*"); }
    // 저장돼 있던 자료 파일(백업·임시·옛 형식 포함)을 모두 지운다
    public static int Wipe(string path) { string[] old = Existing(path); foreach (string f in old) File.Delete(f); return old.Length; }

    static Vault FirstUse(IWin32Window owner, string path, out Data data)
    {
        data = null;
        if (Existing(path).Length > 0 && MessageBox.Show(owner,
                "[처음 사용하기]를 하면 이 컴퓨터에 저장되어 있던\n모든 과목 · 학생 점수 · 결석 자료가 지워집니다.\n\n지운 자료는 되살릴 수 없습니다.\n정말 모두 지우고 새로 시작할까요?",
                "저장된 자료가 모두 지워집니다", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return null;
        while (true)
        {
            string[] a = Prompt.Ask(owner, "새 암호 만들기",
                "앞으로 프로그램을 열 때마다 이 암호를 넣어야 합니다.\n\n※ 암호를 잊으면 찾을 방법이 없습니다. 개발자도 알 수 없고,\n   저장한 자료는 다시 볼 수 없게 됩니다. 꼭 기억해 두세요.",
                new string[] { "새 암호 (" + MinLen + "자 이상)", "한 번 더 입력" }, new string[] { "", "" }, true);
            if (a == null) return null;
            if (a[0].Length < MinLen) { MessageBox.Show(owner, "암호는 " + MinLen + "자 이상으로 만들어 주세요.", "새 암호 만들기"); continue; }
            if (a[0] != a[1]) { MessageBox.Show(owner, "두 칸에 적은 암호가 서로 다릅니다. 다시 적어 주세요.", "새 암호 만들기"); continue; }
            Wipe(path);
            Vault v = Vault.Create(a[0]); data = new Data(); v.Save(path, data); return v;
        }
    }

    // 로그인에 성공하면 Vault, 창을 닫으면 null
    public static Vault Run(string path, out Data data, string shotPath)
    {
        Data got = null; Vault vault = null; bool exists = File.Exists(path);
        Form f = new Form();
        f.Text = "최소 성취수준 도우미"; f.Font = new Font("맑은 고딕", 11f); f.StartPosition = FormStartPosition.CenterScreen;
        f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false;
        f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowLayoutPanel t = new FlowLayoutPanel(); t.FlowDirection = FlowDirection.TopDown; t.AutoSize = true; t.WrapContents = false; t.Padding = new Padding(22, 16, 22, 16);
        Label title = new Label(); title.Text = "최소 성취수준 도우미"; title.Font = new Font("맑은 고딕", 17f, FontStyle.Bold); title.AutoSize = true; title.Margin = new Padding(3, 3, 3, 14);
        title.Margin = new Padding(3, 3, 3, 0);
        Label ver = new Label(); ver.Text = "버전 " + Updater.Ver; ver.AutoSize = true; ver.ForeColor = Color.DimGray; ver.Margin = new Padding(5, 0, 3, 12);
        t.Controls.Add(title); t.Controls.Add(ver);
        TextBox pw = new TextBox(); Label err = new Label(); Button go = new Button(), first = new Button(), chat = new Button(), upd = new Button();
        int w = (int)(470 * f.Font.Height / 20f);
        foreach (Button b in new Button[] { go, first, chat, upd }) { b.Width = w; b.Height = f.Font.Height + 18; b.Margin = new Padding(3, 5, 3, 5); }
        // 새 버전이 있으면 맨 위에 초록 버튼이 나타난다 (확인은 뒤에서 조용히, 인터넷이 없으면 아무 일도 없음)
        Updater.Info found = FakeUpdate;
        upd.Visible = false; upd.BackColor = Color.FromArgb(46, 139, 87); upd.ForeColor = Color.White; upd.FlatStyle = FlatStyle.Flat; upd.Font = new Font(f.Font, FontStyle.Bold);
        MethodInvoker offer = delegate { upd.Text = "⬆ 새 버전 " + found.Version + " 이 나왔습니다 - 눌러서 업데이트"; upd.Visible = true; };
        upd.Click += delegate
        {
            upd.Enabled = false; upd.Text = "내려받는 중입니다..."; f.Cursor = Cursors.WaitCursor; Application.DoEvents();
            string bad = Updater.Apply(found); f.Cursor = Cursors.Default;
            if (bad != null) { MessageBox.Show(f, bad, "업데이트"); upd.Visible = false; return; }
            MessageBox.Show(f, "버전 " + found.Version + " 으로 바꿨습니다. 프로그램을 다시 엽니다.\n저장한 자료와 암호는 그대로입니다.", "업데이트");
            Process.Start(Application.ExecutablePath); Environment.Exit(0);
        };
        t.Controls.Add(upd);
        if (found != null) offer();
        else if (shotPath == null) f.Shown += delegate
        {
            Thread th = new Thread(delegate() { Updater.Info i = Updater.Check(); if (i == null) return; found = i; try { f.BeginInvoke(offer); } catch { } });
            th.IsBackground = true; th.Start();
        };
        if (exists)
        {
            Label l = new Label(); l.Text = "암호를 입력하세요"; l.AutoSize = true;
            pw.UseSystemPasswordChar = true; pw.Width = w;
            go.Text = "들어가기"; go.Font = new Font(f.Font, FontStyle.Bold);
            err.AutoSize = true; err.ForeColor = Color.FromArgb(190, 40, 40); err.MaximumSize = new Size(w, 0);
            go.Click += delegate
            {
                string why = "암호가 맞지 않습니다. 다시 적어 주세요.";
                f.Cursor = Cursors.WaitCursor;
                try { vault = Vault.Open(path, pw.Text, out got); } catch (Exception ex) { vault = null; why = "자료 파일을 열지 못했습니다: " + ex.Message; }
                f.Cursor = Cursors.Default;
                if (vault != null) { f.DialogResult = DialogResult.OK; return; }
                err.Text = why; pw.SelectAll(); pw.Focus();
            };
            t.Controls.Add(l); t.Controls.Add(pw); t.Controls.Add(go); t.Controls.Add(err); f.AcceptButton = go;
        }
        else
        {
            Label l = new Label(); l.Text = "처음 사용하시는군요.\n먼저 이 프로그램을 열 때 쓸 암호를 만들어 주세요."; l.AutoSize = true; l.Margin = new Padding(3, 3, 3, 8);
            t.Controls.Add(l); first.Font = new Font(f.Font, FontStyle.Bold);
        }
        Label warn = new Label(); warn.AutoSize = true; warn.ForeColor = Color.DimGray; warn.MaximumSize = new Size(w, 0); warn.Margin = new Padding(3, 12, 3, 6);
        warn.Text = exists ? "암호를 잊으면 찾을 방법이 없습니다. (개발자도 알 수 없습니다)\n그때는 [처음 사용하기]로 모두 지우고 새로 시작해야 합니다."
                           : "학생 점수는 이 암호로 잠겨 저장되어, 암호 없이는 읽을 수 없습니다.";
        first.Text = exists ? "처음 사용하기 (자료를 지우고 새 암호 만들기)" : "처음 사용하기 (새 암호 만들기)";
        first.Click += delegate { vault = FirstUse(f, path, out got); if (vault != null) f.DialogResult = DialogResult.OK; };
        chat.Text = "💬 개발자에게 문의하기 (카카오톡 오픈채팅)"; chat.BackColor = Color.FromArgb(254, 229, 0); chat.FlatStyle = FlatStyle.Flat;
        chat.Click += delegate { OpenChat(); };
        t.Controls.Add(warn); t.Controls.Add(first); t.Controls.Add(chat);
        f.Controls.Add(t);
        if (shotPath != null)
        {
            f.StartPosition = FormStartPosition.Manual; f.Show(); Application.DoEvents();
            using (Bitmap bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(shotPath); }
            f.Close(); data = null; return null;
        }
        f.ShowDialog(); data = got; return vault;
    }
}

// ───────────── 작은 입력 창 ─────────────
public static class Prompt
{
    public static string ShotPath;
    public static string[] Ask(IWin32Window owner, string title, string note, string[] labels, string[] defaults) { return Ask(owner, title, note, labels, defaults, false); }
    public static string[] Ask(IWin32Window owner, string title, string note, string[] labels, string[] defaults, bool mask)
    {
        Form f = new Form();
        f.Text = title; f.Font = new Font("맑은 고딕", 11f); f.StartPosition = FormStartPosition.CenterParent;
        f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false;
        f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; f.Padding = new Padding(14);
        TableLayoutPanel t = new TableLayoutPanel(); t.AutoSize = true; t.ColumnCount = 2; t.Dock = DockStyle.Fill;
        if (note != null)
        {
            Label n = new Label(); n.Text = note; n.AutoSize = true; n.MaximumSize = new Size(520, 0); n.Margin = new Padding(3, 3, 3, 12);
            t.Controls.Add(n); t.SetColumnSpan(n, 2);
        }
        TextBox[] box = new TextBox[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            Label l = new Label(); l.Text = labels[i]; l.AutoSize = true; l.Anchor = AnchorStyles.Left; l.Margin = new Padding(3, 8, 10, 8);
            box[i] = new TextBox(); box[i].Text = defaults[i]; box[i].Width = 220; box[i].Anchor = AnchorStyles.Left; box[i].UseSystemPasswordChar = mask;
            t.Controls.Add(l); t.Controls.Add(box[i]);
        }
        FlowLayoutPanel b = new FlowLayoutPanel(); b.AutoSize = true; b.FlowDirection = FlowDirection.RightToLeft; b.Dock = DockStyle.Fill; b.Margin = new Padding(3, 12, 3, 3);
        Button cancel = new Button(); cancel.Text = "취소"; cancel.AutoSize = true; cancel.DialogResult = DialogResult.Cancel; cancel.Padding = new Padding(10, 3, 10, 3);
        Button ok = new Button(); ok.Text = "확인"; ok.AutoSize = true; ok.DialogResult = DialogResult.OK; ok.Padding = new Padding(14, 3, 14, 3);
        b.Controls.Add(cancel); b.Controls.Add(ok);
        t.Controls.Add(b); t.SetColumnSpan(b, 2);
        f.Controls.Add(t); f.AcceptButton = ok; f.CancelButton = cancel;
        if (ShotPath != null)   // 화면 점검용: 창을 그림으로 남기고 기본값으로 [확인]
        {
            f.StartPosition = FormStartPosition.Manual; f.Show(); Application.DoEvents();
            using (Bitmap bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(ShotPath); }
            f.Close(); return defaults;
        }
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        string[] res = new string[box.Length];
        for (int i = 0; i < box.Length; i++) res[i] = mask ? box[i].Text : box[i].Text.Trim();
        return res;
    }
}

// ───────────── 화면 ─────────────
public class MainForm : Form
{
    static readonly Color[] Back = { Color.White, Color.White, Color.FromArgb(255, 243, 176), Color.FromArgb(255, 213, 170), Color.FromArgb(248, 190, 190) };
    readonly string path; readonly Vault vault; Data data; Subject cur; bool busy;
    ComboBox cbSubj = new ComboBox(); Label hint = new Label(); TabControl tabs = new TabControl(); Label status = new Label();
    TextBox tbName = new TextBox(); RadioButton rbCommon = new RadioButton(), rbElective = new RadioButton();
    NumericUpDown nudTotal = new NumericUpDown(); Label lbSum = new Label();
    DataGridView gItems, gScores, gList; CheckBox cbCaution = new CheckBox(); Label lbCount = new Label();
    public string ShotDir; public const string ShotPw = "shot-test-pw";

    public MainForm(string dataPath, Data d, Vault v)
    {
        path = dataPath; data = d; vault = v;
        Text = "최소 성취수준 도우미"; Font = new Font("맑은 고딕", 11f);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Size = new Size(Math.Min(1280, wa.Width - 40), Math.Min(800, wa.Height - 30));
        MinimumSize = new Size(Math.Min(900, Width), Math.Min(560, Height));
        StartPosition = FormStartPosition.CenterScreen;

        // 위쪽: 과목 고르기
        FlowLayoutPanel top = new FlowLayoutPanel(); top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(10, 10, 10, 6); top.BackColor = Color.FromArgb(234, 241, 251);
        Label l = new Label(); l.Text = "과목"; l.AutoSize = true; l.Font = new Font(Font, FontStyle.Bold); l.Margin = new Padding(3, 9, 6, 3);
        cbSubj.DropDownStyle = ComboBoxStyle.DropDownList; cbSubj.Width = 260; cbSubj.Margin = new Padding(3, 6, 10, 3);
        cbSubj.SelectedIndexChanged += delegate { if (!busy) SelectSubject(cbSubj.SelectedIndex); };
        top.Controls.Add(l); top.Controls.Add(cbSubj);
        top.Controls.Add(Btn("＋ 새 과목 만들기", delegate { NewSubject(); }));
        top.Controls.Add(Btn("이 과목 삭제", delegate { DeleteSubject(); }));
        top.Controls.Add(Btn("판정 기준", delegate { EditCriteria(); }));
        top.Controls.Add(Btn("사용 방법", delegate { ShowHelp(); }));
        Button chat = Btn("💬 개발자에게 문의", delegate { Login.OpenChat(); }); chat.BackColor = Color.FromArgb(254, 229, 0); chat.FlatStyle = FlatStyle.Flat;
        top.Controls.Add(chat);
        hint.AutoSize = true; hint.ForeColor = Color.FromArgb(190, 40, 40); hint.Font = new Font(Font, FontStyle.Bold); hint.Margin = new Padding(12, 10, 3, 3);
        hint.Text = "← 먼저 [＋ 새 과목 만들기]를 눌러 주세요";
        top.Controls.Add(hint);

        status.Dock = DockStyle.Bottom; status.AutoSize = false; status.Height = Font.Height + 10; status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Color.DimGray; status.Padding = new Padding(8, 0, 0, 0);
        status.Text = "입력하면 자동으로 저장됩니다.   학생 이름은 입력하지 않습니다(반·번호만).   자료는 암호로 잠겨 저장됩니다.";

        tabs.Dock = DockStyle.Fill; tabs.Font = new Font("맑은 고딕", 12f, FontStyle.Bold); tabs.Padding = new Point(18, 7);
        tabs.TabPages.Add(PageSetup()); tabs.TabPages.Add(PageScores()); tabs.TabPages.Add(PageList());
        foreach (TabPage p in tabs.TabPages) { p.Font = Font; p.Padding = new Padding(10); p.UseVisualStyleBackColor = true; }
        tabs.SelectedIndexChanged += delegate { if (tabs.SelectedIndex == 1) FillScores(); if (tabs.SelectedIndex == 2) FillList(); };

        Controls.Add(tabs); Controls.Add(status); Controls.Add(top);
        RefreshSubjects(data.Subjects.Count > 0 ? 0 : -1);
        Shown += delegate { if (ShotDir != null) Shoot(); };
    }

    Button Btn(string text, EventHandler h)
    {
        Button b = new Button(); b.Text = text; b.AutoSize = true; b.Padding = new Padding(8, 3, 8, 3); b.Margin = new Padding(3, 3, 6, 3); b.Click += h; return b;
    }
    Label Lbl(string text, bool gray)
    {
        Label l = new Label(); l.Text = text; l.AutoSize = true; l.Margin = new Padding(3, 8, 8, 3); if (gray) l.ForeColor = Color.DimGray; return l;
    }
    DataGridView Grid()
    {
        DataGridView g = new DataGridView();
        g.Dock = DockStyle.Fill; g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false; g.RowHeadersVisible = false;
        g.BackgroundColor = Color.White; g.BorderStyle = BorderStyle.FixedSingle; g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect; g.RowTemplate.Height = Font.Height + 12;
        g.EnableHeadersVisualStyles = false; g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 232, 242);
        g.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold); g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        g.DataError += delegate { };
        return g;
    }
    DataGridViewColumn Col(DataGridView g, string head, int w, bool readOnly, bool fill)
    {
        DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn(); c.HeaderText = head; c.ReadOnly = readOnly; c.SortMode = DataGridViewColumnSortMode.NotSortable;
        c.Width = (int)(w * Font.Height / 20f);
        if (fill) c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; else c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        if (readOnly) c.DefaultCellStyle.ForeColor = Color.FromArgb(40, 40, 40);
        g.Columns.Add(c); return c;
    }

    // ── ① 과목·평가 설정
    TabPage PageSetup()
    {
        TabPage p = new TabPage("① 과목·평가 설정");
        TableLayoutPanel t = new TableLayoutPanel(); t.Dock = DockStyle.Fill; t.ColumnCount = 1;
        FlowLayoutPanel a = new FlowLayoutPanel(); a.AutoSize = true; a.Dock = DockStyle.Fill;
        tbName.Width = 240; tbName.Margin = new Padding(3, 5, 3, 3);
        tbName.Leave += delegate { if (cur == null) return; string n = tbName.Text.Trim(); if (n == "") { tbName.Text = cur.Name; return; } cur.Name = n; Save(); RefreshSubjects(data.Subjects.IndexOf(cur)); };
        a.Controls.Add(Lbl("과목 이름", false)); a.Controls.Add(tbName);

        FlowLayoutPanel b = new FlowLayoutPanel(); b.AutoSize = true; b.Dock = DockStyle.Fill;
        rbCommon.Text = "공통과목  (출석률 + 학업성취율 둘 다 확인)"; rbCommon.AutoSize = true; rbCommon.Margin = new Padding(3, 5, 16, 3);
        rbElective.Text = "선택과목  (출석률만 확인, 점수는 참고)"; rbElective.AutoSize = true; rbElective.Margin = new Padding(3, 5, 3, 3);
        rbCommon.CheckedChanged += delegate { if (busy || cur == null) return; cur.UseAchv = rbCommon.Checked; Save(); };
        b.Controls.Add(Lbl("과목 종류", false)); b.Controls.Add(rbCommon); b.Controls.Add(rbElective);

        FlowLayoutPanel c = new FlowLayoutPanel(); c.AutoSize = true; c.Dock = DockStyle.Fill;
        nudTotal.Maximum = 999; nudTotal.Width = 90; nudTotal.Margin = new Padding(3, 5, 3, 3);
        nudTotal.ValueChanged += delegate { if (busy || cur == null) return; cur.TotalLessons = (int)nudTotal.Value; Save(); };
        c.Controls.Add(Lbl("한 학기 전체 수업 횟수", false)); c.Controls.Add(nudTotal);
        c.Controls.Add(Lbl("예) 주 3시간 × 17주 = 51.   0으로 두면 출결은 판정하지 않습니다.", true));

        FlowLayoutPanel e = new FlowLayoutPanel(); e.AutoSize = true; e.Dock = DockStyle.Fill; e.Margin = new Padding(3, 12, 3, 3);
        Label h = Lbl("평가 항목", false); h.Font = new Font(Font, FontStyle.Bold);
        e.Controls.Add(h); e.Controls.Add(Btn("＋ 평가 추가", delegate { AddItem(); })); e.Controls.Add(Btn("선택한 평가 삭제", delegate { DeleteItem(); }));
        lbSum.AutoSize = true; lbSum.Margin = new Padding(12, 9, 3, 3); e.Controls.Add(lbSum);

        gItems = Grid();
        Col(gItems, "평가 이름 (예: 1차 지필평가, 수행평가 1)", 300, false, true);
        DataGridViewComboBoxColumn k = new DataGridViewComboBoxColumn(); k.HeaderText = "구분"; k.Items.Add("지필평가"); k.Items.Add("수행평가");
        k.Width = (int)(140 * Font.Height / 20f); k.SortMode = DataGridViewColumnSortMode.NotSortable; k.FlatStyle = FlatStyle.Flat; gItems.Columns.Add(k);
        Col(gItems, "만점", 110, false, false); Col(gItems, "반영비율(%)", 140, false, false);
        gItems.CurrentCellDirtyStateChanged += delegate { if (gItems.CurrentCell is DataGridViewComboBoxCell && gItems.IsCurrentCellDirty) gItems.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        gItems.CellValueChanged += delegate(object s, DataGridViewCellEventArgs ev) { if (!busy && ev.RowIndex >= 0) ItemEdited(ev.RowIndex, ev.ColumnIndex); };

        t.Controls.Add(a); t.Controls.Add(b); t.Controls.Add(c); t.Controls.Add(e); t.Controls.Add(gItems);
        for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(t); return p;
    }

    void FillSetup()
    {
        busy = true;
        tbName.Text = cur.Name; rbCommon.Checked = cur.UseAchv; rbElective.Checked = !cur.UseAchv;
        nudTotal.Value = Math.Max(0, Math.Min(999, cur.TotalLessons));
        gItems.Rows.Clear();
        foreach (Item it in cur.Items) gItems.Rows.Add(it.Name, it.Kind == "지필평가" ? "지필평가" : "수행평가", Calc.F(it.Max), Calc.F(it.Weight));
        busy = false; ShowSum();
    }
    void ShowSum()
    {
        double sum = 0; foreach (Item it in cur.Items) sum += it.Weight;
        if (sum > 100 + 1e-9) { lbSum.ForeColor = Color.FromArgb(190, 40, 40); lbSum.Text = "반영비율 합계 " + Calc.F(sum) + "% - 100%를 넘었습니다. 고쳐 주세요."; }
        else if (sum > 100 - 1e-9) { lbSum.ForeColor = Color.FromArgb(20, 120, 60); lbSum.Text = "반영비율 합계 100%  ✔"; }
        else { lbSum.ForeColor = Color.DimGray; lbSum.Text = "반영비율 합계 " + Calc.F(sum) + "%  (나머지 " + Calc.F(100 - sum) + "%는 '아직 안 본 평가'로 계산)"; }
    }
    void AddItem()
    {
        if (cur == null) return;
        Item it = new Item(); it.Name = "평가 " + (cur.Items.Count + 1); cur.Items.Add(it);
        foreach (Row r in cur.Rows) r.Scores.Add(null);
        Save(); FillSetup();
        gItems.CurrentCell = gItems.Rows[gItems.Rows.Count - 1].Cells[0]; gItems.Focus();
    }
    void DeleteItem()
    {
        if (cur == null || gItems.CurrentCell == null) return;
        int i = gItems.CurrentCell.RowIndex;
        if (MessageBox.Show(this, "'" + cur.Items[i].Name + "' 평가와, 이 평가에 입력한 점수를 모두 지웁니다.\n지울까요?", "평가 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        cur.Items.RemoveAt(i); foreach (Row r in cur.Rows) r.Scores.RemoveAt(i);
        Save(); FillSetup();
    }
    void ItemEdited(int row, int col)
    {
        Item it = cur.Items[row]; string v = Convert.ToString(gItems.Rows[row].Cells[col].Value).Trim(); string bad = null; double n;
        if (col == 0) { if (v == "") bad = "평가 이름을 적어 주세요."; else it.Name = v; }
        else if (col == 1) it.Kind = v;
        else if (!double.TryParse(v, out n)) bad = "숫자만 적어 주세요.";
        else if (col == 2)
        {
            double big = 0; foreach (Row r in cur.Rows) if (r.Scores[row].HasValue) big = Math.Max(big, r.Scores[row].Value);
            if (n <= 0) bad = "만점은 0보다 커야 합니다."; else if (n < big) bad = "이미 입력된 점수(" + Calc.F(big) + "점)보다 만점이 작습니다."; else it.Max = n;
        }
        else { if (n < 0 || n > 100) bad = "반영비율은 0 ~ 100 사이로 적어 주세요."; else it.Weight = n; }
        if (bad != null)
        {
            Say(bad, "입력 확인");
            busy = true; gItems.Rows[row].Cells[col].Value = col == 0 ? it.Name : col == 2 ? Calc.F(it.Max) : Calc.F(it.Weight); busy = false; return;
        }
        Save(); ShowSum();
    }

    // ── ② 점수·결석 입력
    TabPage PageScores()
    {
        TabPage p = new TabPage("② 점수·결석 입력");
        TableLayoutPanel t = new TableLayoutPanel(); t.Dock = DockStyle.Fill; t.ColumnCount = 1;
        FlowLayoutPanel a = new FlowLayoutPanel(); a.AutoSize = true; a.Dock = DockStyle.Fill;
        a.Controls.Add(Btn("＋ 학생 추가 (반·번호)", delegate { AddStudents(); })); a.Controls.Add(Btn("선택한 학생 삭제", delegate { DeleteStudents(); }));
        a.Controls.Add(Lbl("칸을 누르고 숫자를 치면 됩니다.  아직 안 본 평가는 비워 두세요.  엑셀에서 복사한 점수는 Ctrl+V 로 붙여넣기.", true));
        gScores = Grid();
        gScores.CellEndEdit += delegate(object s, DataGridViewCellEventArgs ev) { ScoreEdited(ev.RowIndex, ev.ColumnIndex); };
        gScores.KeyDown += delegate(object s, KeyEventArgs ev)
        {
            if (ev.Control && ev.KeyCode == Keys.V) { Paste(); ev.Handled = true; }
            else if (ev.KeyCode == Keys.Delete) { foreach (DataGridViewCell c in gScores.SelectedCells) if (!c.ReadOnly) { c.Value = ""; ScoreEdited(c.RowIndex, c.ColumnIndex); } ev.Handled = true; }
        };
        t.Controls.Add(a); t.Controls.Add(gScores);
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(t); return p;
    }

    void FillScores()
    {
        if (cur == null) return;
        busy = true; gScores.Rows.Clear(); gScores.Columns.Clear();
        Col(gScores, "반", 60, true, false); Col(gScores, "번호", 60, true, false);
        foreach (Item it in cur.Items) Col(gScores, it.Name + "\n(" + Calc.F(it.Max) + "점, " + Calc.F(it.Weight) + "%)", 112, false, false);
        Col(gScores, "결석\n횟수", 64, false, false);
        Col(gScores, "지금까지\n득점률", 84, true, false); Col(gScores, "남은 평가\n필요 득점률", 100, true, false); Col(gScores, "판정", 84, true, false);
        DataGridViewColumn why = Col(gScores, "설명", 300, true, true);   // 좁으면 가로 스크롤이 생기도록 최소 너비를 둔다
        why.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft; why.MinimumWidth = (int)(300 * Font.Height / 20f);
        cur.Rows.Sort(delegate(Row x, Row y) { return x.Ban != y.Ban ? x.Ban.CompareTo(y.Ban) : x.Num.CompareTo(y.Num); });
        foreach (Row r in cur.Rows)
        {
            int i = gScores.Rows.Add(); DataGridViewRow g = gScores.Rows[i]; g.Tag = r;
            g.Cells[0].Value = r.Ban; g.Cells[1].Value = r.Num;
            for (int k = 0; k < cur.Items.Count; k++) g.Cells[2 + k].Value = r.Scores[k].HasValue ? Calc.F(r.Scores[k].Value) : "";
            g.Cells[2 + cur.Items.Count].Value = r.Absent == 0 ? "" : r.Absent.ToString();
            PaintRow(g);
        }
        busy = false;
    }
    void PaintRow(DataGridViewRow g)
    {
        Res x = Calc.Judge(data, cur, (Row)g.Tag); int n = cur.Items.Count;
        g.Cells[3 + n].Value = x.Cur.HasValue ? Calc.F(x.Cur.Value) + "%" : "";
        g.Cells[4 + n].Value = x.Achv == 1 ? "확보" : x.Need.HasValue ? (x.Need.Value > 100 ? "불가능" : Calc.F(x.Need.Value) + "%") : "";
        g.Cells[5 + n].Value = x.Label; g.Cells[6 + n].Value = x.Reason;
        for (int c = 3 + n; c <= 6 + n; c++) g.Cells[c].Style.BackColor = Back[x.Level];
        g.Cells[5 + n].Style.Font = x.Level >= 3 ? new Font(Font, FontStyle.Bold) : Font;
    }
    // 한 칸에 값 넣기 (직접 입력·붙여넣기 공용). 잘못된 값이면 이유를 돌려주고 아무것도 바꾸지 않는다.
    string SetCell(Row r, int col, string text)
    {
        int n = cur.Items.Count; text = (text ?? "").Trim();
        if (col >= 2 && col < 2 + n)
        {
            Item it = cur.Items[col - 2]; double v;
            if (text == "") { r.Scores[col - 2] = null; return null; }
            if (!double.TryParse(text, out v) || v < 0 || v > it.Max) return "'" + it.Name + "' 점수는 0 ~ " + Calc.F(it.Max) + " 사이 숫자로 적어 주세요.";
            r.Scores[col - 2] = v; return null;
        }
        if (col == 2 + n)
        {
            int a;
            if (text == "") { r.Absent = 0; return null; }
            if (!int.TryParse(text, out a) || a < 0 || a > 999) return "결석 횟수는 0 이상의 숫자로 적어 주세요.";
            r.Absent = a; return null;
        }
        return "";   // 입력 칸이 아님
    }
    void ShowCell(DataGridViewRow g, int col)
    {
        Row r = (Row)g.Tag; int n = cur.Items.Count;
        g.Cells[col].Value = col == 2 + n ? (r.Absent == 0 ? "" : r.Absent.ToString()) : r.Scores[col - 2].HasValue ? Calc.F(r.Scores[col - 2].Value) : "";
    }
    void ScoreEdited(int row, int col)
    {
        if (busy || row < 0) return;
        DataGridViewRow g = gScores.Rows[row];
        string bad = SetCell((Row)g.Tag, col, Convert.ToString(g.Cells[col].Value));
        if (bad == "") return;
        if (bad != null) Say(bad, "입력 확인");
        busy = true; ShowCell(g, col); PaintRow(g); busy = false; Save();
    }
    void Paste()
    {
        if (gScores.CurrentCell == null) return;
        string[] lines = Clipboard.GetText().Replace("\r", "").TrimEnd('\n').Split('\n');
        int r0 = gScores.CurrentCell.RowIndex, c0 = gScores.CurrentCell.ColumnIndex, ok = 0, bad = 0, outside = 0;
        busy = true;
        for (int i = 0; i < lines.Length; i++)
        {
            string[] cells = lines[i].Split('\t');
            for (int k = 0; k < cells.Length; k++)
            {
                if (r0 + i >= gScores.Rows.Count || c0 + k > 2 + cur.Items.Count || c0 + k < 2) { outside++; continue; }
                DataGridViewRow g = gScores.Rows[r0 + i];
                if (SetCell((Row)g.Tag, c0 + k, cells[k]) == null) { ok++; ShowCell(g, c0 + k); } else bad++;
            }
            if (r0 + i < gScores.Rows.Count) PaintRow(gScores.Rows[r0 + i]);
        }
        busy = false; Save();
        if (bad + outside > 0) Say(ok + "칸을 붙여넣었습니다.\n\n" + (bad > 0 ? "숫자가 아니거나 만점을 넘어 건너뛴 칸: " + bad + "\n" : "") + (outside > 0 ? "표 밖이라 들어가지 않은 칸: " + outside + "\n" : ""), "붙여넣기");
    }
    void AddStudents()
    {
        if (cur == null) return;
        string[] a = Prompt.Ask(this, "학생 추가", "반과 번호 범위를 적으면 한꺼번에 추가됩니다.\n한 명만 추가하려면 시작과 끝 번호를 같게 적으세요.", new string[] { "반", "시작 번호", "끝 번호" }, new string[] { "1", "1", "25" });
        if (a == null) return;
        int ban, from, to;
        if (!int.TryParse(a[0], out ban) || !int.TryParse(a[1], out from) || !int.TryParse(a[2], out to) || ban < 1 || ban > 99 || from < 1 || to < from || to > 99)
        { Say("반은 1~99, 번호는 1~99 사이 숫자로, 끝 번호는 시작 번호 이상으로 적어 주세요.", "입력 확인"); return; }
        int added = 0, dup = 0;
        for (int n = from; n <= to; n++)
        {
            if (cur.Rows.Exists(delegate(Row r) { return r.Ban == ban && r.Num == n; })) { dup++; continue; }
            Row row = new Row(); row.Ban = ban; row.Num = n; foreach (Item it in cur.Items) row.Scores.Add(null);
            cur.Rows.Add(row); added++;
        }
        Save(); FillScores();
        if (dup > 0) Say(added + "명 추가했습니다. (이미 있던 " + dup + "명은 그대로 두었습니다)", "학생 추가");
    }
    void DeleteStudents()
    {
        List<Row> pick = new List<Row>();
        foreach (DataGridViewCell c in gScores.SelectedCells) { Row r = (Row)c.OwningRow.Tag; if (!pick.Contains(r)) pick.Add(r); }
        if (pick.Count == 0) return;
        if (MessageBox.Show(this, "선택한 학생 " + pick.Count + "명과 입력한 점수를 이 과목에서 지웁니다.\n지울까요?", "학생 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        foreach (Row r in pick) cur.Rows.Remove(r);
        Save(); FillScores();
    }

    // ── ③ 지도 대상 학생 (모든 과목)
    TabPage PageList()
    {
        TabPage p = new TabPage("③ 지도 대상 학생 보기");
        TableLayoutPanel t = new TableLayoutPanel(); t.Dock = DockStyle.Fill; t.ColumnCount = 1;
        FlowLayoutPanel a = new FlowLayoutPanel(); a.AutoSize = true; a.Dock = DockStyle.Fill;
        lbCount.AutoSize = true; lbCount.Font = new Font(Font, FontStyle.Bold); lbCount.Margin = new Padding(3, 9, 16, 3);
        cbCaution.Text = "'주의' 단계도 함께 보기"; cbCaution.Checked = true; cbCaution.AutoSize = true; cbCaution.Margin = new Padding(3, 7, 16, 3);
        cbCaution.CheckedChanged += delegate { FillList(); };
        a.Controls.Add(lbCount); a.Controls.Add(cbCaution); a.Controls.Add(Btn("엑셀 파일로 저장", delegate { Export(); }));
        gList = Grid(); gList.ReadOnly = true;
        Col(gList, "반", 60, true, false); Col(gList, "번호", 60, true, false); Col(gList, "과목", 180, true, false); Col(gList, "판정", 90, true, false);
        Col(gList, "이유", 400, true, true).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        t.Controls.Add(a); t.Controls.Add(Lbl("모든 과목에서 걸린 학생을 한꺼번에 보여 줍니다.  빨강 = 미도달(이미 기준 미달),  주황 = 위험,  노랑 = 주의", true)); t.Controls.Add(gList);
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(t); return p;
    }

    // 화면과 파일 저장이 같은 목록을 쓴다: {반, 번호, 과목, 단계, 판정, 이유}
    public static List<object[]> Flagged(Data d, int minLevel)
    {
        List<object[]> list = new List<object[]>();
        foreach (Subject s in d.Subjects) foreach (Row r in s.Rows)
        {
            Res x = Calc.Judge(d, s, r);
            if (x.Level >= minLevel) list.Add(new object[] { r.Ban, r.Num, s.Name, x.Level, x.Label, x.Reason });
        }
        list.Sort(delegate(object[] x, object[] y)
        {
            int c = ((int)y[3]).CompareTo((int)x[3]); if (c != 0) return c;
            c = ((int)x[0]).CompareTo((int)y[0]); if (c != 0) return c;
            c = ((int)x[1]).CompareTo((int)y[1]); return c != 0 ? c : string.CompareOrdinal((string)x[2], (string)y[2]);
        });
        return list;
    }
    void FillList()
    {
        gList.Rows.Clear();
        Dictionary<string, int> worst = new Dictionary<string, int>();
        foreach (object[] o in Flagged(data, 2))
        {
            string key = o[0] + "-" + o[1]; int lv = (int)o[3];
            if (!worst.ContainsKey(key) || worst[key] < lv) worst[key] = lv;
            if (lv == 2 && !cbCaution.Checked) continue;
            int i = gList.Rows.Add(o[0], o[1], o[2], o[4], o[5]);
            gList.Rows[i].DefaultCellStyle.BackColor = Back[lv];
        }
        int[] cnt = new int[5]; foreach (int v in worst.Values) cnt[v]++;
        lbCount.Text = worst.Count == 0 ? "걸린 학생이 없습니다." : "미도달 " + cnt[4] + "명  ·  위험 " + cnt[3] + "명  ·  주의 " + cnt[2] + "명";
    }
    void Export()
    {
        SaveFileDialog dlg = new SaveFileDialog(); dlg.Filter = "엑셀에서 열리는 파일 (*.csv)|*.csv"; dlg.FileName = "지도대상학생_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        StringBuilder sb = new StringBuilder("반,번호,과목,판정,이유\r\n");
        foreach (object[] o in Flagged(data, cbCaution.Checked ? 2 : 3))
            sb.Append(o[0]).Append(',').Append(o[1]).Append(",\"").Append(((string)o[2]).Replace("\"", "\"\"")).Append("\",").Append(o[4]).Append(",\"").Append(((string)o[5]).Replace("\"", "\"\"")).Append("\"\r\n");
        try { File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true)); Say("저장했습니다.\n" + dlg.FileName + "\n\n※ 이 엑셀 파일은 암호로 잠기지 않습니다. 보관·전달에 주의해 주세요.", "저장"); }
        catch (Exception e) { MessageBox.Show(this, "저장하지 못했습니다. 그 파일이 엑셀에 열려 있으면 닫고 다시 해 주세요.\n\n" + e.Message, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    // ── 과목
    void RefreshSubjects(int select)
    {
        busy = true; cbSubj.Items.Clear(); foreach (Subject s in data.Subjects) cbSubj.Items.Add(s.Name);
        cbSubj.SelectedIndex = select; busy = false; SelectSubject(select);
    }
    void SelectSubject(int i)
    {
        cur = i >= 0 ? data.Subjects[i] : null;
        tabs.Enabled = cur != null; hint.Visible = cur == null;
        if (cur == null) { busy = true; tbName.Text = ""; gItems.Rows.Clear(); gScores.Rows.Clear(); busy = false; lbSum.Text = ""; FillList(); return; }
        FillSetup(); FillScores(); FillList();
    }
    void NewSubject()
    {
        string[] a = Prompt.Ask(this, "새 과목 만들기", "과목 이름을 적어 주세요. (예: 공통국어1, 공통수학1 3반)", new string[] { "과목 이름" }, new string[] { "" });
        if (a == null || a[0] == "") return;
        Subject s = new Subject(); s.Name = a[0];
        string[][] basic = { new string[] { "1차 지필평가", "지필평가", "30" }, new string[] { "2차 지필평가", "지필평가", "30" }, new string[] { "수행평가 1", "수행평가", "20" }, new string[] { "수행평가 2", "수행평가", "20" } };
        foreach (string[] b in basic) { Item it = new Item(); it.Name = b[0]; it.Kind = b[1]; it.Weight = double.Parse(b[2]); s.Items.Add(it); }
        data.Subjects.Add(s); Save(); RefreshSubjects(data.Subjects.Count - 1); tabs.SelectedIndex = 0;
    }
    void DeleteSubject()
    {
        if (cur == null) return;
        if (MessageBox.Show(this, "'" + cur.Name + "' 과목과 그 안의 학생 " + cur.Rows.Count + "명 점수를 모두 지웁니다.\n지울까요?", "과목 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        data.Subjects.Remove(cur); Save(); RefreshSubjects(data.Subjects.Count > 0 ? 0 : -1);
    }
    void EditCriteria()
    {
        string[] a = Prompt.Ask(this, "판정 기준", "지침이 바뀌었을 때만 고치세요.\n· 학업성취율 기준: 이 점수(100점 환산) 미만이면 미도달\n· 주의 여유: 지금까지 득점률이 '기준 + 여유' 미만이면 '주의'로 표시\n· 출석률 기준은 2/3로 고정입니다.",
            new string[] { "학업성취율 기준 (%)", "주의 여유 (%p)" }, new string[] { Calc.F(data.Cut), Calc.F(data.Margin) });
        if (a == null) return;
        double cut, m;
        if (!double.TryParse(a[0], out cut) || !double.TryParse(a[1], out m) || cut <= 0 || cut > 100 || m < 0 || m > 60)
        { Say("기준은 1~100, 여유는 0~60 사이 숫자로 적어 주세요.", "입력 확인"); return; }
        data.Cut = cut; data.Margin = m; Save(); if (cur != null) FillScores(); FillList();
    }
    void ShowHelp()
    {
        MessageBox.Show(this,
            "① 과목·평가 설정\n   과목 종류(공통/선택), 한 학기 수업 횟수, 평가 항목과 반영비율을 적습니다.\n\n" +
            "② 점수·결석 입력\n   [＋ 학생 추가]로 반·번호를 넣고, 본 평가의 점수와 결석 횟수를 적습니다.\n   아직 안 본 평가는 비워 두세요. 엑셀에서 복사해 Ctrl+V 로 붙여넣을 수 있습니다.\n\n" +
            "③ 지도 대상 학생 보기\n   모든 과목에서 기준에 못 미칠 수 있는 학생이 색깔과 함께 나옵니다.\n\n" +
            "판정의 뜻\n   미도달(빨강): 남은 평가를 다 맞아도 기준(" + Calc.F(data.Cut) + "점)이 안 되거나, 결석이 허용 횟수를 넘음\n" +
            "   위험(주황): 지금까지 득점률이 기준 미만이거나, 결석이 허용 횟수의 70% 이상\n" +
            "   주의(노랑): 득점률이 기준에 가깝거나, 결석이 허용 횟수의 절반 이상\n\n" +
            "기준: 공통과목 = 출석률 2/3 이상 + 학업성취율 40% 이상, 선택과목 = 출석률 2/3 이상\n" +
            "학생 이름은 입력하지 않습니다. 자료는 이 컴퓨터의 파일 하나에만, 암호로 잠겨 저장됩니다.\n" +
            "암호를 잊으면 찾을 방법이 없습니다([처음 사용하기]로 모두 지우고 새로 시작).\n" + path,
            "사용 방법", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void Save()
    {
        try { vault.Save(path, data); }
        catch (Exception e) { status.ForeColor = Color.FromArgb(190, 40, 40); status.Text = "저장하지 못했습니다: " + e.Message; return; }
        status.ForeColor = Color.DimGray; status.Text = "저장됨 " + DateTime.Now.ToString("HH:mm:ss") + "   학생 이름은 입력하지 않습니다(반·번호만).   자료는 암호로 잠겨 저장됩니다.";
    }

    void Say(string text, string title)
    {
        if (ShotDir != null) File.AppendAllText(Path.Combine(ShotDir, "messages.txt"), "[" + title + "] " + text.Replace("\n", " | ") + "\r\n", Encoding.UTF8);
        else MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // 화면 점검(--shot): 실제 처리 함수를 그대로 불러 붙여넣기·잘못된 입력·학생 추가를 해 보고 그림으로 남긴다
    void Shoot()
    {
        if (cur == null) { using (Bitmap b = new Bitmap(Width, Height)) { DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(Path.Combine(ShotDir, "empty.png")); } Close(); return; }
        tabs.SelectedIndex = 1; Application.DoEvents();
        Clipboard.SetText("30\t\t12\r\nabc\t50\t25\r\n");
        gScores.CurrentCell = gScores.Rows[5].Cells[2]; Paste();
        gScores.Rows[0].Cells[2].Value = "999"; ScoreEdited(0, 2);
        gScores.Rows[0].Cells[2 + cur.Items.Count].Value = "3"; ScoreEdited(0, 2 + cur.Items.Count);
        Prompt.ShotPath = Path.Combine(ShotDir, "dialog.png"); AddStudents(); Prompt.ShotPath = null;
        for (int i = 0; i < tabs.TabCount; i++)
        {
            tabs.SelectedIndex = i; Application.DoEvents();
            using (Bitmap b = new Bitmap(Width, Height)) { DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(Path.Combine(ShotDir, "tab" + (i + 1) + ".png")); }
        }
        // 저장된 파일을 암호로 다시 열어, 화면에서 한 조작이 파일에 그대로 들어갔는지 적어 둔다
        Data re; StringBuilder sb = new StringBuilder();
        sb.Append("[reopen wrong pw] ").AppendLine(Vault.Open(path, ShotPw + "x", out re) == null ? "refused" : "OPENED");
        sb.Append("[reopen right pw] ").AppendLine(Vault.Open(path, ShotPw, out re) == null ? "REFUSED" : "opened");
        foreach (Row r in re.Subjects[0].Rows) if (r.Num == 1 || r.Num == 6 || r.Num == 25)
        {
            sb.Append(r.Ban + "-" + r.Num + " absent=" + r.Absent + " scores=");
            foreach (double? x in r.Scores) sb.Append(x.HasValue ? Calc.F(x.Value) : "_").Append(' ');
            sb.AppendLine();
        }
        File.AppendAllText(Path.Combine(ShotDir, "messages.txt"), sb.ToString(), Encoding.UTF8);
        Close();
    }
}

public static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    static string DataPath()
    {
        string name = "최소성취_자료.dat", here = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        if (File.Exists(here)) return here;
        try { string probe = here + ".probe"; File.WriteAllText(probe, ""); File.Delete(probe); return here; }
        catch
        {   // exe 옆에 쓸 수 없으면(읽기 전용 폴더 등) 내 문서로
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "최소성취수준 도우미");
            Directory.CreateDirectory(dir); return Path.Combine(dir, name);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        string dataPath = null, shot = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--selftest") return SelfTest(args[i + 1]);
            if (args[i] == "--data") dataPath = args[i + 1];
            if (args[i] == "--shot") shot = args[i + 1];
            // 아래는 개발자용
            if (args[i] == "--keygen") return Updater.KeyGen(args[i + 1]);
            if (args[i] == "--release" && i + 2 < args.Length) return Updater.Release(args[i + 1], args[i + 2]);
            if (args[i] == "--update-url") Updater.Base = args[i + 1];
            if (args[i] == "--update-test")
            {   // 화면 없이 실제 확인·교체 함수를 그대로 돌리고 결과를 파일에 적는다
                Updater.Info u = Updater.Check(); string r = u == null ? "no update" : (Updater.Apply(u) ?? "updated to " + u.Version);
                File.WriteAllText(args[i + 1], "was " + Updater.Ver + ": " + r.Replace("\n", " | "), Encoding.UTF8); return 0;
            }
        }
        try { File.Delete(Application.ExecutablePath + ".old"); } catch { }   // 지난 업데이트가 남긴 옛 exe
        SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Data d; Vault v;
        if (shot != null)
        {   // 화면 점검: --data 는 암호 없는 예시 JSON. 점검용 암호로 잠근 파일을 만들어 그 파일로 돌린다
            d = dataPath != null && File.Exists(dataPath) ? Store.Parse(File.ReadAllText(dataPath, Encoding.UTF8)) : new Data();
            dataPath = Path.Combine(shot, "shot-data.dat"); Login.Wipe(dataPath);
            Data none; Login.Run(dataPath, out none, Path.Combine(shot, "login-first.png"));
            v = Vault.Create(MainForm.ShotPw); v.Save(dataPath, d);
            Login.FakeUpdate = new Updater.Info(); Login.FakeUpdate.Version = "9.9";
            Login.Run(dataPath, out none, Path.Combine(shot, "login.png")); Login.FakeUpdate = null;
        }
        else
        {
            if (dataPath == null) dataPath = DataPath();
            v = Login.Run(dataPath, out d, null);
            if (v == null) return 0;
        }
        MainForm f = new MainForm(dataPath, d, v); f.ShotDir = shot;
        Application.Run(f);
        return 0;
    }

    // ───────────── 자체 점검: 기대값은 손 계산 ─────────────
    static int fails; static StringBuilder log = new StringBuilder();
    static void Eq(string what, object got, object want)
    {
        bool ok = Convert.ToString(got) == Convert.ToString(want);
        if (!ok) fails++;
        log.AppendLine((ok ? "ok   " : "FAIL ") + what + (ok ? "" : "  got=" + got + " want=" + want));
    }
    static int Find(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++) { int k = 0; while (k < needle.Length && hay[i + k] == needle[k]) k++; if (k == needle.Length) return i; }
        return -1;
    }
    static Row R(int absent, params double?[] sc) { Row r = new Row(); r.Ban = 1; r.Num = 1; r.Absent = absent; r.Scores.AddRange(sc); return r; }

    static int SelfTest(string outFile)
    {
        Data d = new Data(); Subject s = new Subject(); s.Name = "t"; s.TotalLessons = 51; d.Subjects.Add(s);
        double[][] spec = { new double[] { 100, 30 }, new double[] { 100, 30 }, new double[] { 50, 20 }, new double[] { 50, 20 } };
        foreach (double[] p in spec) { Item it = new Item(); it.Max = p[0]; it.Weight = p[1]; s.Items.Add(it); }
        {   // 지필1 20/100 -> 6점, 수행1 10/50 -> 4점: 10점/50% = 20%, 남은 50%에서 30점 필요 = 60%
            Res x = Calc.Judge(d, s, R(0, 20, null, 10, null));
            Eq("earned", Calc.F(x.Earned), "10"); Eq("cur", Calc.F(x.Cur.Value), "20"); Eq("need", Calc.F(x.Need.Value), "60"); Eq("level risk", x.Level, 3);
        }
        {   // 지필1 30 -> 9점, 지필2 0 -> 0, 수행1 0 -> 0: 9점/80%, 남은 20%로 31점은 불가능
            Res x = Calc.Judge(d, s, R(0, 30, 0, 0, null)); Eq("impossible", x.Level, 4); Eq("impossible label", x.Label, "미도달");
        }
        {   // 지필1 45 -> 13.5점/30% = 45%: 기준(40)은 넘지만 여유(10) 안쪽 = 주의, 필요 26.5/70 = 37.9%
            Res x = Calc.Judge(d, s, R(0, 45, null, null, null)); Eq("caution", x.Level, 2); Eq("caution need", Calc.F(x.Need.Value), "37.9");
        }
        { Res x = Calc.Judge(d, s, R(0, 50, null, null, null)); Eq("50% safe", x.Level, 1); }                    // 15점/30% = 50%
        { Res x = Calc.Judge(d, s, R(0, 100, 40, null, null)); Eq("42 secured", x.Achv, 1); Eq("secured level", x.Level, 1); }   // 30+12 = 42
        { Res x = Calc.Judge(d, s, R(0, 40, 40, 20, 20)); Eq("exactly 40 passes", x.Level, 1); }                 // 12+12+8+8 = 40
        { Res x = Calc.Judge(d, s, R(0, 40, 40, 20, 19.5)); Eq("39.8 final fails", x.Level, 4); }                // 12+12+8+7.8
        { Res x = Calc.Judge(d, s, R(0, null, null, null, null)); Eq("no scores label", x.Label, "점수 없음"); }
        // 출결: 51회 -> 허용 17. 50회 -> 허용 16 (17번 결석이면 33/50 < 2/3)
        Eq("abs 17/51 risk", Calc.Judge(d, s, R(17, 100, 100, 50, 50)).Level, 3);
        Eq("abs 18/51 fail", Calc.Judge(d, s, R(18, 100, 100, 50, 50)).Level, 4);
        Eq("abs 9/51 caution", Calc.Judge(d, s, R(9, 100, 100, 50, 50)).Level, 2);
        Eq("abs 8/51 safe", Calc.Judge(d, s, R(8, 100, 100, 50, 50)).Level, 1);
        s.TotalLessons = 50;
        Eq("abs 16/50 ok", Calc.Judge(d, s, R(16, 100, 100, 50, 50)).Level, 3);
        Eq("abs 17/50 fail", Calc.Judge(d, s, R(17, 100, 100, 50, 50)).Level, 4);
        s.TotalLessons = 0; Eq("no lessons no att", Calc.Judge(d, s, R(40, 100, 100, 50, 50)).Level, 1);
        // 선택과목: 성취율은 판정에 안 들어가고 참고로만
        s.UseAchv = false; s.TotalLessons = 51;
        { Res x = Calc.Judge(d, s, R(0, 0, 0, 0, 0)); Eq("elective ignores achv", x.Level, 1); Eq("elective note", x.Reason.StartsWith("(참고)"), true); }
        Eq("elective att fail", Calc.Judge(d, s, R(18, 100, 100, 50, 50)).Level, 4);
        s.UseAchv = true;
        // 반영비율을 일부만 등록한 과목: 지필 30%만 있고 12/100 -> 3.6점, 남은 70%에서 36.4점 = 52%
        { Subject p = new Subject(); Item it = new Item(); it.Weight = 30; p.Items.Add(it); Res x = Calc.Judge(d, p, R(0, 12)); Eq("partial need", Calc.F(x.Need.Value), "52"); Eq("partial level", x.Level, 3); }
        {   // 업데이트 서명: 맞는 파일만 통과. 1바이트 바뀐 exe, 다른 버전 이름, 남의 키로 한 서명은 거절
            string priv, pub, priv2; byte[] exe = Encoding.ASCII.GetBytes("MZ-pretend-exe-bytes");
            using (RSACryptoServiceProvider r = new RSACryptoServiceProvider(2048)) { priv = r.ToXmlString(true); pub = r.ToXmlString(false); }
            using (RSACryptoServiceProvider r = new RSACryptoServiceProvider(2048)) priv2 = r.ToXmlString(true);
            byte[] sig = Updater.Sign(priv, "1.3", exe), bent = (byte[])exe.Clone(); bent[5] ^= 1;
            Eq("sig ok", Updater.Verify(pub, "1.3", exe, sig), true); Eq("sig bent exe", Updater.Verify(pub, "1.3", bent, sig), false);
            Eq("sig other version", Updater.Verify(pub, "1.4", exe, sig), false); Eq("sig other key", Updater.Verify(pub, "1.3", exe, Updater.Sign(priv2, "1.3", exe)), false);
            Eq("sig garbage key", Updater.Verify("not a key", "1.3", exe, sig), false);
        }
        // 저장 -> 다시 읽기
        string dir =Path.Combine(Path.GetTempPath(), "minach-selftest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, "자료.dat"); Data e;
        s.Name = "MARKER-과목"; s.Rows.Add(R(3, 20, null, 10.5, null)); d.Cut = 41;
        Vault v = Vault.Create("correct horse 암호"); v.Save(tmp, d); byte[] first = File.ReadAllBytes(tmp);
        v.Save(tmp, d); byte[] second = File.ReadAllBytes(tmp);
        {   // 맞는 암호: 그대로 돌아온다
            Vault o = Vault.Open(tmp, "correct horse 암호", out e); Eq("open right pw", o != null, true);
            Eq("roundtrip cut", e.Cut, 41); Eq("roundtrip blank", e.Subjects[0].Rows[0].Scores[1].HasValue, false);
            Eq("roundtrip score", e.Subjects[0].Rows[0].Scores[2], 10.5); Eq("roundtrip absent", e.Subjects[0].Rows[0].Absent, 3);
            Eq("roundtrip name", e.Subjects[0].Name, "MARKER-과목"); Eq("flagged list", MainForm.Flagged(e, 2).Count, 1); Eq("bak kept", File.Exists(tmp + ".bak"), true);
            o.Save(tmp, e); Data e2; Eq("reopened vault saves readable", Vault.Open(tmp, "correct horse 암호", out e2) != null, true);
        }
        {   // 틀린 암호(한 글자 차이, 빈 암호): 열리지 않고 자료도 안 나온다
            Data n; Eq("wrong pw refused", Vault.Open(tmp, "correct horse 암호 ", out n), null); Eq("wrong pw no data", n, null);
            Eq("empty pw refused", Vault.Open(tmp, "", out n), null);
        }
        {   // 파일 안에 과목 이름·점수가 글자로 보이지 않고, 같은 자료도 저장할 때마다 다른 바이트
            Eq("no plaintext name", Find(first, Encoding.UTF8.GetBytes("MARKER")), -1); Eq("no plaintext json", Find(first, Encoding.UTF8.GetBytes("Scores")), -1);
            Eq("no plaintext score", Find(first, Encoding.UTF8.GetBytes("10.5")), -1);
            Eq("positive control: finder works", Find(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(d)), Encoding.UTF8.GetBytes("MARKER")) >= 0, true);
            Eq("same size", first.Length, second.Length); int same = 0; for (int i = 40; i < first.Length; i++) if (first[i] == second[i]) same++;
            Eq("fresh iv each save", same < (first.Length - 40) / 4, true);
        }
        {   // 손댄 파일·잘린 파일·엉뚱한 파일: 예외 없이 거절
            Data n; byte[] t = (byte[])second.Clone(); t[60] ^= 1; File.WriteAllBytes(tmp, t); Eq("tampered refused", Vault.Open(tmp, "correct horse 암호", out n), null);
            File.WriteAllBytes(tmp, new byte[] { 1, 2, 3 }); Eq("truncated refused", Vault.Open(tmp, "correct horse 암호", out n), null);
            File.WriteAllText(tmp, new string('x', 500)); Eq("garbage refused", Vault.Open(tmp, "correct horse 암호", out n), null);
        }
        {   // 처음 사용하기의 삭제: 자료 파일 계열은 전부, 다른 파일은 그대로
            File.WriteAllText(Path.Combine(dir, "자료.json"), "old"); File.WriteAllText(Path.Combine(dir, "남의파일.txt"), "keep");
            Eq("wipe count", Login.Wipe(tmp), 3); Eq("wipe left only other file", string.Join(",", Array.ConvertAll(Directory.GetFiles(dir), Path.GetFileName)), "남의파일.txt");
        }
        Directory.Delete(dir, true);
        log.AppendLine(fails == 0 ? "ALL PASSED" : "FAILED: " + fails);
        File.WriteAllText(outFile, log.ToString(), Encoding.UTF8);
        return fails == 0 ? 0 : 1;
    }
}
