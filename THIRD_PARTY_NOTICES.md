# Third party notices

## ECDICT

Source: [skywind3000/ECDICT](https://github.com/skywind3000/ECDICT).

The dictionary installer uses revision `bc015ed2e24a7abef49fc6dbbb7fe32c1dadaf8b`. The original `ecdict.csv` SHA-256 is `1a6947e04785db63613a92e14903cdae7954f7e84860b10e68e5c7cbb3f9c3cf`.

ECDICT is distributed under its upstream MIT license, reproduced in [licenses/ECDICT-LICENSE.txt](licenses/ECDICT-LICENSE.txt). The full CSV and generated dictionaries are downloaded or built locally and are not tracked in this repository. A package containing ECDICT-derived data must include its license and attribution.

## Wiktionary

English and Chinese entries, including English IPA pronunciation, are retrieved from [English Wiktionary](https://en.wiktionary.org/). Displayed entries retain the originating page link; pronunciation attribution is available in its tooltip. Wiktionary text is generally provided under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), subject to the source page's terms and attribution requirements. Cached source text remains third-party content; it is not relicensed as Lexi code.

## Open English WordNet

[Open English WordNet](https://en-word.net/downloads), edition 2025+, by the Open English WordNet Team, derived from Princeton WordNet, is available under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Lexi indexes the official WN-LMF XML, preserving definitions and examples while formatting them for lookup. This community-maintained derivative is distinct from the original Princeton release. The pinned source and checksum appear in `dictionaries/catalog.json`.

## CC-CEDICT

[CC-CEDICT](https://www.mdbg.net/chinese/dictionary?page=cc-cedict), published by MDBG and maintained by the CC-CEDICT community, continues CEDICT by Paul Andrew Denisowski. The current official download used here declares [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). Lexi indexes the original UTF-8 data into simplified/traditional lookup keys; the resulting dictionary data remains under CC BY-SA 4.0, separate from the code license. `provenance.json` records the source URL and exact downloaded checksum. The installer rejects an unrecognized license header.

## Ministry of Education revised Mandarin dictionary

中華民國教育部（Ministry of Education, R.O.C.）。《重編國語辭典修訂本》（版本編號：2015_20260929）網址：https://dict.revised.moe.edu.tw/

Source: [official public release](https://language.moe.gov.tw/001/Upload/Files/site_content/M0001/respub/index.html). License: [CC BY-ND 3.0 Taiwan](https://creativecommons.org/licenses/by-nd/3.0/tw/legalcode), together with the [official usage notice](https://language.moe.gov.tw/001/Upload/Files/site_content/M0001/respub/reviseddict_10312.pdf).

Lexi preserves every original nonempty row field, original headword, definition and pronunciation. Simplified strings are auxiliary search keys only; displayed original text is not converted, translated or rewritten. These entries are excluded from AI evidence. The installed package retains the original archive and the complete usage notice as `original-source.zip` and `MOE-USAGE.pdf`. Retain both when distributing that data. The full original fields remain accessible in the entry's expander. Original glyph-image assets are not rendered by this version; textual field references remain unchanged.

## National Academy for Educational Research terminology

國家教育研究院 2026《海事學術名詞》、《造船工程學術名詞》、《電子計算機學術名詞》、《兩岸對照名詞-計算機學術名詞》、《電機工程學術名詞》、《物理學學術名詞》及《數學學術名詞》，2026-10 下載快照。此開放資料依政府資料開放授權條款（Open Government Data License）進行公眾釋出，使用者於遵守本條款各項規定之前提下，得利用之。

Sources: [maritime terms](https://data.gov.tw/dataset/15339), [shipbuilding terms](https://data.gov.tw/dataset/15399), [official terminology service](https://terms.naer.edu.tw/). License: [Open Government Data License v1.0](https://data.gov.tw/license). Lexi retains the original English/Chinese pairs and adds alias and simplified lookup keys. These are terminology correspondences, not full explanatory dictionary definitions. AI explanations remain separately labeled. The source files and checksums are recorded in `dictionaries/catalog.json`.

Additional science and technology sources: [computer science](https://data.gov.tw/dataset/15416), [cross-strait computing terms](https://data.gov.tw/dataset/15275), [electrical engineering](https://data.gov.tw/dataset/15418), [physics](https://data.gov.tw/dataset/15317), [mathematics](https://data.gov.tw/dataset/15443). The same license applies. Cross-strait entries retain both regional translations with separate labels, and both are indexed for lookup. No source terminology is silently replaced by another region's equivalent.

## Model clients and generated output

Claude Code and Codex are separately installed official applications. Their terms, authentication and usage limits remain with their respective providers. They are not bundled in this repository. AI-generated explanations and examples are labeled as generated, with available evidence distinguished from unverified output.

## Speech services

Windows speech uses the system's installed SAPI components and voices; Lexi does not redistribute them. Optional [OpenAI TTS](https://developers.openai.com/api/docs/guides/text-to-speech) and [Grok TTS](https://docs.x.ai/developers/model-capabilities/audio/text-to-speech) use user-configured API credentials and are subject to those providers' service terms and billing. Speech is labeled synthetic; generated audio remains local user data and is not included in source or release packages.

## Test data and icon

The small CSV in `tests/fixtures` is synthetic test data created for this project. It is not a subset of a user's query history. The application icon is generated by `build-icon.ps1` using geometric shapes.
