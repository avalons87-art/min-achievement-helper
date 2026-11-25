// 새 버전 내보내기:   node release.js 1.3
//   App.cs 의 버전을 고침 -> 빌드 -> 자체 점검 -> 서명해서 dist/ 에 놓음 -> (git 저장소면) 커밋·푸시
//   푸시가 끝나면 각 PC 의 프로그램이 시작 화면에서 새 버전을 알아챈다.
// 버전을 안 적으면 지금 버전 그대로 다시 빌드·서명만 한다.
const fs = require('fs'), os = require('os'), path = require('path');
const { execFileSync } = require('child_process');

const dir = __dirname, src = path.join(dir, 'App.cs'), exe = path.join(dir, '최소성취수준 도우미.exe');
const key = process.env.MINACH_KEY || path.join(os.homedir(), 'Documents', '최소성취-도우미-서명키', 'private-key.xml');
const csc = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe';
const run = (file, args) => execFileSync(file, args, { cwd: dir, stdio: 'inherit' });
const die = (msg) => { console.error('STOP: ' + msg); process.exit(1); };

let code = fs.readFileSync(src, 'utf8');
const m = code.match(/public const string Ver = "([\d.]+)";/);
if (!m) die('App.cs 에서 버전 줄을 찾지 못했습니다');
const ver = process.argv[2] || m[1];
if (!/^\d+\.\d+$/.test(ver)) die('버전은 1.3 같은 모양으로 적어 주세요: ' + ver);
if (!fs.existsSync(key)) die('서명 키가 없습니다: ' + key);
if (ver !== m[1]) fs.writeFileSync(src, code.replace(m[0], `public const string Ver = "${ver}";`));

run(csc, ['/nologo', '/codepage:65001', '/target:winexe', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
  '/r:System.Web.Extensions.dll', '/out:' + exe, src]);

const report = path.join(os.tmpdir(), 'minach-selftest.txt');
fs.rmSync(report, { force: true });
try { run(exe, ['--selftest', report]); } catch (e) { die('자체 점검 실패\n' + (fs.existsSync(report) ? fs.readFileSync(report, 'utf8') : e.message)); }
const lines = fs.readFileSync(report, 'utf8').trim().split(/\r?\n/);
if (lines[lines.length - 1] !== 'ALL PASSED') die('자체 점검 결과가 이상합니다: ' + lines[lines.length - 1]);

try { run(exe, ['--release', key, path.join(dir, 'dist')]); } catch (e) { die('서명 실패 (exe 안의 공개키와 서명 키가 짝이 아니면 종료 코드 3): ' + e.message); }
console.log(`v${ver}: 자체 점검 ${lines.length - 1}개 통과, dist/app.exe + dist/version.txt 준비됨`);

if (fs.existsSync(path.join(dir, '.git'))) {
  run('git', ['add', '-A']);
  run('git', ['commit', '-m', 'v' + ver]);
  run('git', ['push']);
  console.log('푸시 완료 - 몇 분 뒤부터 각 PC 시작 화면에 업데이트 버튼이 뜹니다');
} else console.log('git 저장소가 아니라 올리지는 않았습니다');
