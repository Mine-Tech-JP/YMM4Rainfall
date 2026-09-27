# 第三者ライブラリの表記

2026年9月12日確認。YMM4Rainfall 1.0.0の本体プロジェクトが参照するライブラリを記載します。これらはYMM4側のDLLを利用し、プロジェクトではPrivate=falseを指定しています。今回確認したRelease出力はYMM4Rainfall.dll、PDB、deps.jsonのみで、以下の第三者DLLは含まれていません。配布物作成時にも内容を確認してください。

YukkuriMovieMaker.PluginおよびYukkuriMovieMaker.ControlsはYMM4の提供物です。本プロジェクトのMPL-2.0は適用されません。利用条件は[ゆっくりMovieMaker4公式サイト](https://manjubox.net/ymm4/)を参照してください。本書はYMM4本体の全依存を網羅するものではありません。

以下の版は開発用YMM4 Lite 4.56.1.0内のDLLのProductVersionから取得しました。ライセンス本文は、そのコミットの公式リポジトリから取得しています。

## Vortice.Windows

対象：Vortice.Direct2D1、Vortice.DirectX、Vortice.DXGI

版：2.1.8-beta、コミット：7adb7e8b5cfb849737a93491122cce68af35c760

[ライセンス原文](https://github.com/amerkoleci/Vortice.Windows/blob/7adb7e8b5cfb849737a93491122cce68af35c760/LICENSE)

```text
The MIT License (MIT)

Copyright (c) Amer Koleci and Contributors

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

```

## Vortice.Mathematics

版：1.4.12、コミット：407974e503

[ライセンス原文](https://github.com/amerkoleci/Vortice.Mathematics/blob/407974e503/LICENSE)

```text
The MIT License (MIT)

Copyright (c) Amer Koleci and contributors.

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

```

## SharpGenTools

対象：SharpGen.Runtime、SharpGen.Runtime.COM

版：2.0.0-beta.10、コミット：795f04e87a8915f56ce540214b308731c3a52e5d

[ライセンス原文](https://github.com/SharpGenTools/SharpGenTools/blob/795f04e87a8915f56ce540214b308731c3a52e5d/LICENSE.txt)

```text
MIT License

Copyright (c) 2010-2017 Alexandre Mutel, 2017 Jeremy Koritzinsky

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
