# GitHub 上传全流程（手把手）

> 适用：你这台 Windows 电脑，仓库已经在 `D:\DSH\BambuMultiLauncher`。
> 现状（已确认）：Git 已安装 2.56.1（`C:\Program Files\Git`，但**不在 PATH 里**，下面的命令先加 PATH）；仓库已经 `git init` 并做了首次提交；`gh` 命令行工具没装（不需要它，网页操作更简单）。
> 目标：把源码和文档传上去，把 exe 和 zip 作为 Release 附件发出去，让别人能下载。

---

## 0. 先搞清楚：什么进仓库，什么不进

| 内容 | 进不进仓库 | 原因 |
|---|---|---|
| `src\` `docs\` `tests\*.cs` `tools\` `.github\` | ✅ 进 | 源码、文档、构建脚本，仓库的主体 |
| `README.md` `LICENSE` `NOTICE` `CHANGELOG.md` `.gitignore` `.gitattributes` `build.bat` | ✅ 进 | 仓库门面（必须放在根目录，GitHub 只认这里） |
| `dist\`（exe 和 zip） | ❌ 不进 | 二进制每次编译都变，让 git 变胖；**用 Release 附件发** |
| `build\`（harness 等编译中间物） | ❌ 不进 | 已在 `.gitignore` 里 |
| `tests\*.exe`（比如 `bambu-sleeper.exe`） | ❌ 不进 | 已在 `.gitignore` 里 |
| 任何 `settings.ini`、日志、`.bak`、凭据文件 | ❌ **绝对不进** | 里面有你的账号路径、登录状态 |

好消息：你的 `.gitignore` 已经写好了（`dist/`、`build/`、`tests/*.exe`、`*.log` 都被排除），我另外加了一个 `.gitattributes`，保证 `build.bat` 在别人电脑上拉下来还是 CRLF 换行——否则别人一编译就报"不是内部或外部命令"。

---

## 1. 准备（5 分钟）

### 1.1 注册/登录 GitHub

打开 https://github.com → 右上角 **Sign up**（没有账号）或 **Sign in**。
建议用户名用英文短名（会出现在下载链接里，例如 `https://github.com/guming/BambuMultiLauncher`）。

### 1.2 决定用哪个邮箱

提交记录里会公开显示邮箱。想隐藏就用 GitHub 的 noreply 邮箱：

1. 网页右上角头像 → **Settings** → 左栏 **Emails**
2. 找到 **Keep my email addresses private**（勾上）
3. 记下页面里给你的 `xxxxx+用户名@users.noreply.github.com`

下面第 2 步会用到。

---

## 2. 设置 Git 身份（只做一次）

打开 **PowerShell**，粘贴（第一行把本机的 Git 临时加进 PATH，因为装了但没进环境变量）：

```powershell
$env:Path += ";C:\Program Files\Git\cmd"
git config --global user.name "你的名字或英文名"
git config --global user.email "你的邮箱或noreply邮箱"
```

检查：

```powershell
git config --global user.name
git config --global user.email
```

两行都有值就对了（之前是空的，所以必须先做这一步，否则提交会失败）。

> 想用中文名也可以，但**建议英文**：`git log` 和 GitHub 上都更好看。

---

## 3. 在本地把项目变成 git 仓库

**这个仓库已经初始化并做过首次提交**（作者信息是占位的，可以按下面改掉后 amend）。首次建立时的步骤留档如下：

```powershell
$env:Path += ";C:\Program Files\Git\cmd"
cd "D:\DSH\BambuMultiLauncher"
git init -b main
```

看到 `Initialized empty Git repository in D:/DSH/BambuMultiLauncher/.git/` 就成功了。

### 3.1 先看一遍"到底会提交什么"（重要）

```powershell
git add .
git status
```

**逐行检查列表**，确认：

- 有 `src/*.cs`、`docs/*.md`、`README.md`、`build.bat`、`.gitignore`、`.gitattributes` ✅
- **没有** `dist/`、`build/`、`tests/*.exe`、`*.log` ✅
- **没有** 任何 `settings.ini`、`BambuNetworkEngine.conf`、`*.bak` ✅

如果看到不该进的东西（比如某个日志），先修 `.gitignore`，再：

```powershell
git rm --cached -r 那个文件或文件夹
```

> 已经 `add` 过的文件，之后再加 `.gitignore` 是**不会**自动排除的，必须 `git rm --cached`。

### 3.2 提交

```powershell
git commit -m "v1.0.5：拓竹多账号启动器（源码 + 文档）"
```

成功会显示 `N files changed, ... insertions(+)`。

---

## 4. 在 GitHub 上建空仓库

1. 打开 https://github.com/new
2. **Repository name**：`BambuMultiLauncher`
3. **Description**（建议双语）：
   `一台 Windows 电脑同时登录多个拓竹账号，各开一个窗口，互不顶号。/ Run multiple Bambu Studio accounts side by side on one Windows PC.`
4. 选 **Public**（别人才能下载；想先私藏就选 Private，但视频引流就没意义了）
5. **下面的三个选项全部不要勾**：
   - ❌ Add a README file
   - ❌ Add .gitignore
   - ❌ Choose a license
   （你本地都有了，勾了会造成"远程已有提交、推送被拒"的麻烦）
6. 点 **Create repository**

建好后页面会显示一段 `git remote add origin ...` 的命令，直接复制用。

---

## 5. 推送到 GitHub

```powershell
cd "D:\DSH\BambuMultiLauncher"
git remote add origin https://github.com/你的用户名/BambuMultiLauncher.git
git push -u origin main
```

### 5.1 第一次推送会弹窗要授权

Git for Windows 自带凭据管理器（GCM），会弹一个浏览器窗口：

1. 点 **Sign in with your browser**
2. 在浏览器里点 **Authorize**
3. 回到 PowerShell，看到 `branch 'main' set up to track 'origin/main'` 就成功了

### 5.2 如果没弹窗、直接报 `Authentication failed`

两种办法，任选：

**办法 A（推荐）用 Personal Access Token 当密码**

1. GitHub 头像 → **Settings** → 左栏拉到底 **Developer settings**
2. **Personal access tokens** → **Tokens (classic)** → **Generate new token (classic)**
3. Note 填 `BambuMultiLauncher`；Expiration 选 90 天；勾选 **repo** 这一项
4. 生成后**立刻复制**那串 `ghp_...`（关掉页面就再也看不到了）
5. 重新 `git push -u origin main`，用户名填 GitHub 用户名，密码粘贴这串 token

**办法 B 用 SSH**（以后不用再输密码）

```powershell
ssh-keygen -t ed25519 -C "你的邮箱"     # 一路回车
Get-Content "$env:USERPROFILE\.ssh\id_ed25519.pub" | Set-Clipboard
```
把剪贴板内容贴到 GitHub → **Settings → SSH and GPG keys → New SSH key**，
然后把远程地址换成 SSH：

```powershell
git remote set-url origin git@github.com:你的用户名/BambuMultiLauncher.git
git push -u origin main
```

刷新网页，能看到 README 渲染出来、文件夹树正常，就算成功了。

---

## 6. 发 Release（让 exe 能下载）

> 为什么不把 exe 提交进仓库：git 不适合存二进制，每次改代码体积都会翻倍；Release 附件是标准做法。

### 6.1 先在本机确认发布包

```powershell
cd "D:\DSH\BambuMultiLauncher"
Get-ChildItem dist\release
```

你应该看到 `dist\release\BambuMultiLauncher-v1.0.5.zip`（192,028 字节），文件名已经是纯 ASCII。**Release 附件名一定用英文**：中文名在某些浏览器下载后会被 URL 编码成一长串 `%E6%8B%93...`，别人看着懵（软件内部名称仍然是中文，不影响使用）。

### 6.2 打标签（tag）

```powershell
git tag -a v1.0.5 -m "v1.0.5：登录状态判据修复（32 字节空壳不再显示已登录）"
git push origin v1.0.5
```

### 6.3 在网页上建 Release

1. 仓库页右侧 → **Releases** → **Create a new release**
2. **Choose a tag** → 选 `v1.0.5`（刚推上去的）
3. **Release title**：`v1.0.5 · 拓竹多账号启动器`
4. **Describe this release**（可直接粘）：

```markdown
## 下载

- **Windows 10 / 11（64 位）**：[BambuMultiLauncher-v1.0.5.zip](附件见下方 Assets)

解压后双击 `拓竹多账号启动器.exe` 即用，无需安装（.NET Framework 4.x 为系统自带）。

## 这个版本改了什么

- 修复：32 字节的空壳凭据被误判为"已登录"，现在按 ≥48 字节判定，主界面与环境报告都会显示"空壳，实际未登录"
- 上一版（v1.0.4）修复了外部测试报告里的 15 个问题：自检在干净目录可 17/17 通过、删掉的账号不再被自动认回、`--datadir=` 写法、相对路径规范化、保留名（NUL 等）、只读配置保存失败提示、重命名重名校验等

## 校验

- `--selftest` 在全新空目录：通过 17 项，失败 0 项，退出码 0
- 详细说明见仓库 `docs/使用手册.md`、`docs/常见问题.md`、`docs/原理与限制.md`
```

5. **Attach binaries**：把 `dist\release\BambuMultiLauncher-v1.0.5.zip` 拖上去
6. 勾选 **Set as the latest release**
7. 点 **Publish release**

完成后，`Releases → v1.0.5 → Assets` 里就有下载按钮了。这个页面地址就是视频里要给的链接：

```
https://github.com/你的用户名/BambuMultiLauncher/releases/latest
```

---

## 7. 把仓库"装修"一下（影响别人敢不敢下载）

在仓库首页点右上角齿轮 **⚙** 编辑 **About**：

- **Description**：见第 4 步的双语描述
- **Website**：可以先留空，或填你的 B站主页
- **Topics**（很重要，别人搜得到）：逐个添加
  `bambu-lab` `bambu-studio` `3d-printing` `multi-account` `windows` `winforms` `csharp` `dotnet-framework` `launcher` `portable-app`
- 勾上 **Releases**、**Packages** 不用

然后确认这几个文件在首页能点到：

- `README.md` 第一屏要有：一句话说明 + 截图 + 下载链接 + 三步上手（现在已经是这样）
- `LICENSE` 能被识别成 MIT（首页右侧会显示 "MIT license"）
- `CHANGELOG.md` 从 v1.0.5 往下排

可选：**Settings → Social preview** 上传一张 1280×640 的封面（用 `docs\img\main.png` 裁一张就行），分享链接时更好看。

---

## 8. 常见报错对照表

| 报错 | 原因 | 解决 |
|---|---|---|
| `fatal: not a git repository` | 不在仓库目录 / 还没 `git init` | `cd "D:\DSH\BambuMultiLauncher"` 后重试 |
| `Author identity unknown` | 没设 `user.name/user.email` | 回到第 2 步 |
| `remote origin already exists` | 远程已加过 | `git remote set-url origin 新地址` |
| `Updates were rejected because the remote contains work...` | 建仓库时勾了 README/LICENSE | `git pull --rebase origin main` 再 `git push`（或删掉远程仓库重建，不勾那三项） |
| `Authentication failed` / `Support for password authentication was removed` | 用了账号密码 | 用 Personal Access Token 当密码（第 5.2 A） |
| `error: RPC failed; HTTP 413` / 推送很慢 | 提交里混进了大文件 | `git count-objects -vH` 看体积；把大文件移出并 `git rm --cached` |
| `warning: LF will be replaced by CRLF` | 换行符自动转换 | 正常提示，`.gitattributes` 已经处理好 `build.bat` 等文件 |
| 中文文件名显示成 `\346\213...` | git 默认转义非 ASCII 文件名 | `git config --global core.quotepath false` |
| 推送卡住不动 | 网络/代理 | 关掉 VPN 或给 git 配代理：`git config --global http.proxy http://127.0.0.1:7890`（按你的代理端口） |

---

## 9. 以后每次更新版本，照这个顺序做

```powershell
cd "D:\DSH\BambuMultiLauncher"

# 1) 改代码后先编译 + 自检（自检目录要是全新的空目录，里面放好测试桩）
build.bat
tools\make-sleeper.bat                                    # 编译 tests\bambu-sleeper.exe
Remove-Item -Recurse -Force D:\DSH\_st_new -ErrorAction SilentlyContinue
New-Item -ItemType Directory D:\DSH\_st_new | Out-Null
Copy-Item tests\bambu-sleeper.exe D:\DSH\_st_new\
cmd /c start /wait "" "dist\拓竹多账号启动器.exe" --selftest "D:\DSH\_st_new"

# 2) 更新 CHANGELOG.md 和版本号（src\Core.cs 的 Version、src\AssemblyInfo.cs 三处）

# 3) 提交
git add -A
git status                      # 再看一眼有没有不该进的文件
git commit -m "v1.0.6：修复 xxx"

# 4) 打 tag 并推送
git tag -a v1.0.6 -m "v1.0.6"
git push
git push origin v1.0.6

# 5) 网页上 Create a new release，选 v1.0.6，上传新的 zip
```

> 小提醒：`git push` 不会推送 tag，tag 要单独 `git push origin v1.0.6`（或者 `git push --tags`）。

---

## 10. 视频 + 仓库互相导流（把流量接住）

1. **README 顶部**加一行视频入口，例如：
   ```markdown
   > 视频演示：B站搜「拓竹多账号启动器」（<视频链接>）
   ```
2. **B站简介/置顶评论**放 `releases/latest` 链接（这个链接永远指向最新版，不用每次改视频文案）。
3. **抖音**只在评论区和主页引导，不给外链。
4. 下载页（Release 说明）里写清楚：**遇到问题先跑「启动诊断」，把报告贴到 Issues**——这样你能收到结构化的问题描述，而不是"用不了"。

---

## 11. 上传前的安全自查（30 秒）

```powershell
cd "D:\DSH\BambuMultiLauncher"
git ls-files | Select-String -Pattern "settings.ini|BambuNetworkEngine|\.bak|\.log|AppData"
```

**必须没有任何输出。** 如果有，说明有敏感文件被跟踪了：

```powershell
git rm --cached "路径"
```

再确认一遍体积（超过 50MB 就该警惕）：

```powershell
git count-objects -vH
```

---

## 12. 检查清单（照着打勾）

- [ ] 设好 `user.name` / `user.email`
- [ ] `git init -b main` + `git add .` + `git status` 里没有 `dist/`、`build/`、`*.log`、`*.ini`、`*.bak`
- [ ] 本地 commit 成功
- [ ] GitHub 网页建了空仓库（**不勾** README / .gitignore / license）
- [ ] `git push -u origin main` 成功，网页能看到代码
- [ ] About 里填了 Description + Topics
- [ ] 打了 `v1.0.5` tag 并推送
- [ ] Release 上传了 `BambuMultiLauncher-v1.0.5.zip`，勾了 Latest
- [ ] `releases/latest` 链接能打开、能下载
- [ ] 视频简介/置顶评论里的链接用的是 `releases/latest`
