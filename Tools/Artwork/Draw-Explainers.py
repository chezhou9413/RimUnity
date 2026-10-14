from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

#用统一网格绘制双语说明图，图片随仓库发布，不进入游戏运行包。
ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'Tools/Steam/Images'
W, H = 1600, 1120
BG, PANEL, LINE = '#101923', '#192735', '#33485A'
WHITE, MUTED, CYAN, GOLD = '#F0F5F7', '#A9BBCB', '#65E1DA', '#F5C77D'
REGULAR = 'C:/Windows/Fonts/msyh.ttc'
BOLD = 'C:/Windows/Fonts/msyhbd.ttc'


#在固定列宽内换行，中文按字符、英文按单词处理。
def wrap(text, font, width):
    lines = []
    for paragraph in text.split('\n'):
        tokens = list(paragraph) if any('\u4e00' <= c <= '\u9fff' for c in paragraph) else paragraph.split(' ')
        joiner = '' if any('\u4e00' <= c <= '\u9fff' for c in paragraph) else ' '
        line = ''
        for token in tokens:
            candidate = line + (joiner if line else '') + token
            if font.getlength(candidate) > width and line:
                lines.append(line)
                line = token
            else:
                line = candidate
        lines.append(line)
    return lines


#封装文字、面板和连接线，保持缩略图可读性。
class Diagram:
    def __init__(self, language, number, title, subtitle):
        self.language = language
        self.image = Image.new('RGB', (W, H), BG)
        self.draw = ImageDraw.Draw(self.image)
        for x in range(0, W, 64):
            self.draw.line((x, 0, x, H), fill='#14202C')
        self.draw.rectangle((64, 58, 72, 92), fill=CYAN)
        self.text(94, 54, 'RIMUNITY', 27, bold=True)
        self.text(1210, 57, f'{number} / OVERVIEW', 22, color=MUTED)
        self.text(64, 120, title, 57, bold=True)
        self.text(66, 207, subtitle, 27, color=MUTED, width=1450)
        self.draw.line((64, 1028, 1536, 1028), fill=LINE, width=2)
        self.text(64, 1052, 'RimWorld 1.7  /  Unity 2022.3 LTS  /  MIT', 22, color=MUTED)

    #绘制有边界检查的文本，避免文字溢出图片。
    def text(self, x, y, text, size=30, color=WHITE, bold=False, width=None):
        font = ImageFont.truetype(BOLD if bold else REGULAR, size)
        lines = wrap(text, font, width) if width else text.split('\n')
        for line in lines:
            assert x + font.getlength(line) <= W - 40, line
            assert y + size * 1.5 < H, line
            self.draw.text((x, y), line, font=font, fill=color)
            y += int(size * 1.55)
        return y

    #面板只承载一个概念，标题和正文使用固定内边距。
    def box(self, bounds, title, body='', accent=CYAN, title_size=32, body_size=27):
        x, y, right, bottom = bounds
        self.draw.rounded_rectangle(bounds, radius=18, fill=PANEL, outline=LINE, width=2)
        self.draw.rounded_rectangle((x + 24, y + 27, x + 29, y + 63), radius=2, fill=accent)
        end = self.text(x + 48, y + 23, title, title_size, accent, True, right - x - 75)
        if body:
            end = self.text(x + 30, end + 14, body, body_size, MUTED, width=right - x - 60)
        assert end < bottom - 10, (title, end, bottom)

    #沿连接方向绘制箭头。
    def arrow(self, start, end, color=CYAN):
        import math
        self.draw.line((*start, *end), fill=color, width=4)
        angle = math.atan2(end[1] - start[1], end[0] - start[0])
        tip = [end]
        for delta in [-0.5, 0.5]:
            tip.append((end[0] - 16 * math.cos(angle + delta), end[1] - 16 * math.sin(angle + delta)))
        self.draw.polygon(tip, fill=color)

    #保存适合网页和 Steam 简介使用的 PNG。
    def save(self, name):
        directory = OUTPUT / self.language
        directory.mkdir(parents=True, exist_ok=True)
        self.image.save(directory / f'{name}.png', optimize=True)


#从制作、导出、交付到调用，说明作者和玩家的分工。
def workflow(lang):
    zh = lang == 'zh-CN'
    d = Diagram(lang, '01', '从 Unity 到游戏，只走一条清晰的路径' if zh else 'From editor to game. One clear workflow.',
                '作者在编辑器制作；内容模组携带资源；玩家只安装模组。' if zh else 'Authors create. Content mods ship the assets. Players only install mods.')
    steps = [
        ('Unity 编辑器', '制作 Prefab、粒子、材质与动画', '原生组件 · 层级 · 脚本字段'),
        ('RimUnity 导出器', '收集依赖，处理支持范围内的 Shader', '稳定 ID · 明确诊断 · 压缩与去重'),
        ('你的内容模组', '携带 catalog、资源文件与游戏侧 DLL', '1.7/Data/RimUnity'),
        ('RimWorld / Waylith', '加载模板 → 实例化 → 播放与交互', 'PrefabLoader.Load → Object.Instantiate'),
    ] if zh else [
        ('Unity Editor', 'Author prefabs, particles, materials and animation', 'Native components / Hierarchies / Fields'),
        ('RimUnity Exporter', 'Collect dependencies; convert supported shaders', 'Stable IDs / Diagnostics / Compression'),
        ('Your content mod', 'Ship catalog, asset files and game-side DLLs', '1.7/Data/RimUnity'),
        ('RimWorld / Waylith', 'Load a template, instantiate, play and interact', 'PrefabLoader.Load → Object.Instantiate'),
    ]
    for i, (title, body, tag) in enumerate(steps):
        y = 300 + i * 168
        d.draw.ellipse((66, y + 35, 132, y + 101), fill=CYAN)
        d.text(84, y + 46, str(i + 1), 28, BG, True)
        if i < 3:
            d.arrow((99, y + 110), (99, y + 193))
        d.box((165, y, 1534, y + 140), title, title_size=31)
        d.text(560, y + 25, body, 27)
        d.text(560, y + 77, tag, 23, MUTED)
    d.save('workflow')


#优势图仅描述已提供的工作方式，不承诺完整 Unity 兼容。
def advantages(lang):
    zh = lang == 'zh-CN'
    d = Diagram(lang, '02', '熟悉的制作方式，可控的游戏接入' if zh else 'Familiar authoring. Explicit game integration.',
                '减少重复搭建，把时间留给内容与表现。' if zh else 'Spend less time rebuilding assets and more time creating content.')
    cards = [
        ('01 / 在 Unity 中调效果', '继续使用组件、层级和 Prefab。\n粒子与动画在编辑器中制作。'),
        ('02 / 使用熟悉的 API', 'GameObject · Transform · MonoBehaviour\n修改类型引用并重新编译脚本。'),
        ('03 / 资源交付更清楚', '依赖自动收集，资源按逻辑路径加载。\n材质随资源交付，无需逐个写 ShaderDef。'),
        ('04 / 开源，边界明确', 'MIT 许可，运行时与导出器源码开放。\n支持范围与限制均有文档。'),
    ] if zh else [
        ('01 / Author in Unity', 'Keep components, hierarchies and prefabs.\nCreate particles and animation in the editor.'),
        ('02 / Familiar APIs', 'GameObject / Transform / MonoBehaviour\nAdapt type references and recompile scripts.'),
        ('03 / Clear asset delivery', 'Collect dependencies; load by logical path.\nNo individual ShaderDef for each material.'),
        ('04 / Open and documented', 'MIT-licensed runtime and exporter source.\nDefined support scope and limitations.'),
    ]
    for i, (title, body) in enumerate(cards):
        x, y = 64 + i % 2 * 752, 310 + i // 2 * 290
        d.box((x, y, x + 720, y + 258), title, body, body_size=27, title_size=31)
    d.text(66, 929, '玩家无需安装 Unity；框架本体不添加种族、建筑或武器。' if zh else
           'Players do not need Unity. The framework itself adds no races, buildings or weapons.', 27, GOLD)
    d.save('advantages')


#性能图展示实际优化路径，不绘制未经测量的 FPS 或倍率柱形图。
def performance(lang):
    zh = lang == 'zh-CN'
    d = Diagram(lang, '03', '减少重复加载，让资源得到复用' if zh else 'Reuse resources. Avoid repeated work.',
                '性能机制示意 · 非 FPS 跑分 · 不代表固定加速倍率' if zh else 'Optimization mechanisms / Not an FPS benchmark / No fixed speedup claim')
    d.box((64, 320, 450, 560), '按路径请求资源' if zh else 'Request an asset',
          'Prefab / Material / Texture\n逻辑路径 → 资源 ID' if zh else 'Prefab / Material / Texture\nLogical path → Asset ID', title_size=28, body_size=23)
    d.box((540, 320, 1000, 560), '模组内资源缓存' if zh else 'Per-mod resource cache',
          '首次：读取 → 解码 → 缓存\n再次：直接取得共享对象' if zh else 'First: read → decode → cache\nLater: reuse the shared object', title_size=28, body_size=23)
    d.box((1090, 320, 1536, 560), '实例拥有自己的状态' if zh else 'Independent instances',
          '共享资源，独立播放与参数\n按需创建独立材质' if zh else 'Shared assets; own playback state\nSeparate materials when needed', title_size=28, body_size=23)
    d.arrow((456, 425), (530, 425))
    d.arrow((1008, 425), (1080, 425))
    cards = [
        ('更小的交付', '可达依赖裁剪\nJSON 压紧 + 大文件 Gzip\n相同内容文件按哈希复用'),
        ('更少的重复读取', '路径与资源 ID 索引\n流式解码资源 JSON\n模组内依赖对象缓存'),
        ('更清楚的测量', '相同资源、规模与分辨率\nCPU / GPU 增量帧时\nGC、内存与加载耗时'),
    ] if zh else [
        ('Smaller delivery', 'Keep reachable dependencies\nCompact JSON + Gzip for large files\nReuse identical files by content hash'),
        ('Less repeated loading', 'Path and asset-ID indexes\nStream JSON deserialization\nCache dependency objects per mod'),
        ('Measure comparable work', 'Same assets, scale and resolution\nIncremental CPU / GPU frame time\nGC, memory and loading time'),
    ]
    for i, (title, body) in enumerate(cards):
        x = 64 + 501 * i
        d.box((x, 600, x + 470, 896), title, body, title_size=28, body_size=23)
    d.text(66, 927, '文件去重 ≠ 跨模组 GPU 对象共享。压缩也会带来解压成本，实际收益需测量。' if zh else
           'File deduplication is not cross-mod GPU sharing. Decompression has a cost; measure the result.', 25, GOLD)
    d.save('performance')


#区分编辑器工具、内容资源、框架运行时与宿主接口。
def architecture(lang):
    zh = lang == 'zh-CN'
    d = Diagram(lang, '04', '一套制作工具，一套游戏运行时' if zh else 'An authoring toolset. A game-side runtime.',
                'Unity 负责制作；RimUnity 负责资源与组件语义；Waylith 执行游戏侧绘制。' if zh else
                'Author in Unity. Load assets and run components in RimUnity. Render through Waylith.')
    d.box((64, 300, 760, 485), '制作端 / Unity Editor' if zh else 'Authoring / Unity Editor',
          '原生组件 → UPM 导出器 → 支持范围内的 Shader 转换' if zh else 'Native components → UPM exporter → Supported shader conversion', body_size=25)
    d.box((840, 300, 1536, 485), '交付端 / 内容模组' if zh else 'Delivery / Content mod',
          'catalog + JSON / Gzip + GLSL\n贴图、字体与游戏侧脚本 DLL' if zh else 'catalog + JSON / Gzip + GLSL\nTextures, fonts and game-side script DLLs', body_size=25)
    d.arrow((768, 385), (830, 385))
    d.arrow((1188, 490), (1188, 510))
    d.box((64, 520, 1536, 800), '运行端 / RimUnity' if zh else 'Runtime / RimUnity', title_size=33)
    cols = [('资源加载', '清单 / 缓存 / 实例化'), ('对象与脚本', '层级 / Mono / 协程'), ('表现系统', '粒子 / UI / 模型 / 动画')] if zh else [
        ('Asset loading', 'Catalog / Cache / Instantiate'), ('Objects & scripts', 'Hierarchy / Mono / Coroutines'), ('Visual systems', 'Particles / UI / Models / Animation')]
    for i, (title, body) in enumerate(cols):
        x = 90 + i * 487
        d.box((x, 612, x + 458, 772), title, body, title_size=28, body_size=23)
    d.arrow((800, 810), (800, 843))
    d.box((64, 853, 1536, 995), '宿主 / RimWorld 1.7 + Waylith' if zh else 'Host / RimWorld 1.7 + Waylith',
          '主循环 / 图形设备 / 材质与绘制提交；Verse 与 Harmony 提供游戏接入。' if zh else
          'Frame loop / Graphics / Materials and draw submission; Verse and Harmony provide game integration.', body_size=25)
    d.save('architecture')


if __name__ == '__main__':
    for language in ['zh-CN', 'en']:
        for build in [workflow, advantages, performance, architecture]:
            build(language)
    print(f'Generated 8 diagrams: {OUTPUT}')
