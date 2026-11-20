# Initiative: Time-series compression literature after Gorilla

> Owner: storage | Last reviewed: 2026-11-20 | Canonical: no (research input to #1802)
>
> Status: literature review only. It does not change the chunk format. It answers one question for the sealed-history chunk codec (issue #1802, epic #1800): which published methods since Facebook Gorilla (VLDB 2015) compress better, by how much, on what data, and which of their ideas are worth trying in our codec.

## Question and short answer

**Question.** What lossless (and, separately, lossy) time-series compression methods published after Gorilla beat it, by how much, on what data, and which ideas fit our codec?

**Short answer.**

1. Every float method after Gorilla reports large gains, but almost all of them come from two ideas: **find the decimal the double came from** (Elf, Camel, DeXOR, Pseudodecimal, ALP, BUFF, Pcodec FloatMult) and **look further back than one value** (Chimp128, TSXor, Pcodec lookback). Our codec already has the first idea in its strongest batch form: per-chunk decimal scaling plus delta or delta-of-delta of the scaled integers, then zstd. The papers' headline numbers are mostly the gap between XOR and decimal scaling, which we have already closed (DoubleBar mean 31.3 bits under Gorilla, 6.5 bits with our decimal mode).
2. What the literature adds on top of what we do is narrower: **per-value exceptions inside a decimal chunk** (ALP), **adaptive bit widths or entropy coding of residuals instead of a fixed bucket code** (Pcodec, BOS, Parquet `DELTA_BINARY_PACKED`, FastLanes), and **typed, column-wise storage of the numbers inside strings** (CLP, LogReducer, Denum).
3. On "real" doubles (computed ratios, our Rate sensors) no specialised float coder does much: in the one same-data comparison (ALP paper, POI-lat) plain zstd is best at 48.1 bits/value, ALP-RD 55.5, Gorilla 66.0. Our Rate values are `sum / elapsed-seconds` from a monotonic clock in the managed collector, so their mantissa is noise; a general compressor takes about 20% off it (28.9 → 23.1 bits/value measured), no lossless method recovers the mantissa.
4. Nothing published targets irregular 100 ns timestamps. The literature confirms our measurement: Gorilla's delta-of-delta buckets assume second or millisecond steps on a regular grid.
5. For HSM in production, String sensors are 65.5% of sealed bytes on one SoftFx production week, measured during #1802 (in the garage shard measured in the storage research they are 0.02%), so the string ideas (typed template variables) matter more than any float paper.

Ranked shortlist: [Shortlist for #1802](#shortlist-for-1802).

## What our codec already does (the baseline for every "fit" verdict)

From `aicontext/features/storage/sealed-history/feature.md` and `src/database/HSMDatabase.SealedHistory/Format/*.cs` on branch `feature/1802-chunk-codec` (not merged when this was written; the verdicts go stale if that format changes), and the measurements in [sensor-history-storage.md](sensor-history-storage.md):

| Column kind | What v1 does | Measured cost (week chunks) |
|---|---|---|
| Integer (keys, tick offsets, counts, status, ...) | Per chunk: constant, or residual order 0 / 1 / 2 (raw, delta, delta-of-delta) chosen by size; residuals zigzag-coded as either LEB128 varints or a bit stream with a fixed 8-bucket prefix code (widths 0, 4, 8, 12, 16, 24, 32, 64 bits, unary bucket index) | `Time` 16–24 bits/row; at 100 ns, variable-length delta-of-delta 21.1 bits vs 59.7 bits for a Gorilla-style bucket code (Rate) |
| Double | Per chunk, smallest of: constant; byte-aligned XOR (a Patas-like layout); **decimal** (one exponent e in 0..15 for the whole chunk, mantissas in an integer column, chosen only if every value round-trips); Gorilla XOR | Double 22.1 bits, DoubleBar mean 6.5, min 3.9, std_dev 4.4; Rate 28.9 (23.1–24.4 after zstd, two runs in the storage research) |
| String | Constant, or per-chunk dictionary of distinct values + integer index column | Strings are 65.5% of sealed bytes on one SoftFx production week, measured during #1802 (in the garage shard measured in the storage research they are 0.02%); ~30-char, every value distinct; template extraction (prototype) gave −21..−26% |
| Whole payload | One zstd frame (level 3) over all column streams, if smaller | Store: 9.5 B/row lossless, 7.5 B/row with `ReceivingTime` at 1 ms |

Data facts that decide fit: doubles are mostly rounded decimals; a changed double changes almost all of its 52 mantissa bits; Rate (computed ratios) is 61% of rows in the measured shard; timestamps are irregular 100 ns ticks (delta-of-delta exactly zero in 7.3% of rows, 69% of rows have a non-zero sub-millisecond part); after values are encoded well, the two timestamps are more than half of a row.

## How to read the numbers below

- "Bits/value" (bpv) is compressed bits per 64-bit double; 64 means no compression. Several papers use "compression ratio" = compressed/original (Elf), others original/compressed (FCBench, TSXor, Pcodec). Each quote below names the convention.
- Each paper uses its own datasets. The one table that puts Gorilla, Chimp, Chimp128, Patas, Pseudodecimal, Elf, ALP and zstd on the same 30 datasets is Table 4 of the ALP paper; it is reproduced in [Same-data comparison](#same-data-comparison-alp-paper-table-4). DeXOR's Table 2 does the same for the XOR/decimal-XOR family on 22 datasets.
- Cross-paper numbers disagree. Example: DeXOR's Table 4 reports ALP at 52.97 bits geomean on DeXOR's datasets, while the ALP paper reports 21.7 bpv on its own; the dataset mixes (DeXOR has many high-precision series) and settings differ. Compare methods inside one table, not across papers.
- None of the papers measures 100 ns timestamps, multi-field records, or a final zstd pass over a mix of columns, which is our setting. Expected effects for HSM below are therefore stated as "measure", not as a number, unless our own measurement exists.

## Floating point

### Gorilla (baseline)

- **Mechanism.** Timestamps: delta-of-delta with a bucketed prefix code (`0` for zero, then `10`/`110`/`1110` + 7/9/12 bits, else 32 bits). Values: XOR with the previous value; `0` if equal, `10` + meaningful bits if they fit in the previous leading/trailing-zero window, else `11` + 5 bits leading zeros + 6 bits length + bits.
- **Reported.** 16 bytes per (timestamp, value) pair down to 1.37 bytes on Facebook's monitoring data, a 12x reduction; about 96% of timestamps compress to one bit and about 51% of values to one bit because they equal the previous value ([Pelkonen et al., PVLDB 8(12) 2015](https://www.vldb.org/pvldb/vol8/p1816-teller.pdf)).
- **Fit.** Already in v1 as double mode 3. Its 1.37 bytes depend on second-resolution regular timestamps and many repeats; our data has neither for timestamps.

### FPC (2009, a pre-Gorilla baseline in most later papers)

- **Mechanism.** Two hash-table predictors (FCM and DFCM) predict the next double; the closer prediction is XORed with the value; leading zero bytes are counted and the remainder stored ([Burtscher and Ratanaworabhan, IEEE TC 58(1) 2009, DOI 10.1109/TC.2008.131](https://doi.org/10.1109/TC.2008.131); [author's page](https://userweb.cs.txstate.edu/~burtscher/research/FPC/)). Mechanism summary taken from the author's page and abstract; the paper itself was not read.
- **Reported against Gorilla.** In Chimp's evaluation FPC and Gorilla are close; Chimp128 is 39% better than FPC and 40% better than Gorilla on average (see Chimp).
- **Fit.** None; our own ClickHouse test already found FPC larger than plain zstd on this data ([storage research](sensor-history-storage.md)).

### Chimp and Chimp128 (PVLDB 2022)

- **Mechanism.** Chimp observes that XORs of changed values rarely have more than 6 trailing zeros. Flag `0x`: trailing zeros > 6, then `0` = identical, or `1` + 3-bit leading-zero class + 6-bit length + center bits. Flag `1x`: trailing zeros <= 6, store all bits after the leading zeros; `10` reuses the previous leading-zero count, `11` writes a new 3-bit class. Leading zeros are rounded down to one of 8 classes (up to 24). Chimp128 XORs with the best of the previous 128 values (most trailing zeros), found in O(1) via a ring buffer plus a 2^14-entry table indexed by the low 14 bits of each value (about 33 KB), and writes a 7-bit back-reference only when the trailing zeros pay for it ([Liakos, Papakonstantinopoulou, Kotidis, PVLDB 15(11) 2022](https://www.vldb.org/pvldb/vol15/p3058-liakos.pdf)).
- **Reported.** On 19 datasets (14 time series: city temperatures, stocks with 1–3 decimals, air/dust sensors, Bitcoin price, ...), Chimp is 9.6% better than Gorilla on average; Chimp128 is 40% better than Gorilla (44% on the 14 time series) and competitive with general-purpose compressors; Chimp128 compresses faster than Gorilla (same paper, Sections 5.2–5.3).
- **Lossless.** Yes. **Code.** [panagiotisl/chimp](https://github.com/panagiotisl/chimp), Java, Apache-2.0.
- **Fit for HSM.** Chimp alone is a better Gorilla; our decimal mode already beats Gorilla by far more on rounded data. The useful part is Chimp128's **window**: a value that equals or nearly equals one of the last 128 values (oscillating states, 0 after a burst) gets a back-reference. Our delta/delta-of-delta of decimal mantissas does not capture A, B, A, B patterns; zstd over a bit stream catches them only partly. Worth trying only if a measurement shows non-consecutive repeats are common (see shortlist item 6).

### Patas (DuckDB)

- **Mechanism.** Byte-aligned variant of Chimp128: stores the non-zero byte sequence of the XOR with the best previous value instead of a bit sequence, trading ratio for decode speed (description from the ALP paper, Section 4; [DuckDB demo page](https://duckdb.org/library/fp-compression/), not read in full).
- **Reported.** 35.5 bpv average in ALP's Table 4 (Gorilla 42.2, Chimp128 28.7). DuckDB replaced Chimp128 and Patas with ALP ([cwida/ALP README](https://github.com/cwida/ALP)).
- **Fit.** Our double mode 1 (XOR bytes against the previous value) is the same idea without the window. Nothing to add.

### TSXor (SPIRE 2021)

- **Mechanism.** A window of the previous W <= 127 values acts as a cache: an exact match is written as its window position; otherwise the value is XORed with the best window entry and a byte-aligned code writes the non-zero bytes (4 bits for trailing zeros in the header byte); an exception code 255 is followed by the raw 8 bytes ([andybbruno/TSXor README](https://github.com/andybbruno/TSXor); paper at [jermp.github.io SPIRE2021.pdf](https://jermp.github.io/assets/pdf/papers/SPIRE2021.pdf), not read).
- **Reported (README, original/compressed).** AMPds2 6.39x vs Gorilla 2.03x; PAMAP 4.85x vs 1.38x; Kinect 1.37x vs 1.41x (Gorilla better); compression much slower than Gorilla (67 vs 704 MB/s on AMPds2), decompression faster.
- **Lossless.** Yes. **Code.** C++, no licence file (GitHub reports none). Numbers are from the README, not the paper.
- **Fit.** Same window idea as Chimp128. Its big wins are on datasets with exact repeats inside the window.

### Elf, Elf+, Elf* and SElf* (PVLDB 2023; arXiv; IEEE IoT Journal 2025)

- **Mechanism.** For a double that came from a decimal with decimal significand count β, Elf proves how many trailing mantissa bits can be set to zero such that rounding the erased value to α decimal places restores the original exactly; it stores β (4 bits) per value and XOR-codes the erased values, whose XORs now have long trailing-zero runs, with a Chimp-like coder tuned for that. Restore = round the decoded value to α places. Elf+ codes β with fewer bits by reusing the previous β ([Li et al., Elf, PVLDB 16(7) 2023](https://www.vldb.org/pvldb/vol16/p1763-li.pdf); [Elf+, arXiv 2306.16053](https://arxiv.org/abs/2306.16053)). Elf* chooses the leading-zero, center-bit and sharing encodings adaptively per dataset with a dynamic program; SElf* is the streaming version ([arXiv 2308.11915](https://arxiv.org/abs/2308.11915); published in IEEE IoT Journal 2025 per DeXOR's reference list).
- **Reported.** Elf, 22 datasets: average relative improvement 12.4% over Chimp128 and 43.9% over Gorilla; on the time-series datasets compressed/original 0.37 vs 0.76 for Gorilla and FPC (51%); better than zstd by 7.5% on time series (Elf paper, Section 6). Elf+ a further 7.6% over Elf (Elf+ abstract). SElf* 9.2% over the best streaming competitor (Elf* abstract). In ALP's Table 4: Elf 23.1 bpv vs ALP 21.7, Gorilla 42.2. Elf compresses about 3x faster than zstd but decompresses slowly; [Beyond Compression (PVLDB 18(11) 2025)](https://www.vldb.org/pvldb/vol18/p4396-hishida.pdf) finds XOR methods such as Elf at the top of compression ratio but with slow decompression.
- **Lossless.** Yes (IEEE bit-exact). **Code.** [Spatio-Temporal-Lab/elf](https://github.com/Spatio-Temporal-Lab/elf), [SElfStar](https://github.com/Spatio-Temporal-Lab/SElfStar), Java; GitHub reports no licence for either.
- **Fit for HSM.** Low. Elf is a streaming, per-value way to recover the decimal precision that our decimal mode recovers per chunk; DeXOR's own analysis shows erasure breaks smoothness between values of different precision, so delta of scaled integers (what we do) is the better batch form for rounded data. No licence, so code cannot be reused anyway.

### Camel (SIGMOD 2024) — secondary source only

- **Mechanism (as described by the DeXOR paper, not read in the original).** Splits a value into integer and fractional parts; the integer part is coded as a difference to the previous one, the fractional part is reduced modulo 2^-α and scaled by 10^α, with a small index; it selects reference values that compress best instead of always XORing with the predecessor ([DOI 10.1145/3698802](https://doi.org/10.1145/3698802); the ACM PDF returned 403 and no author repository was found).
- **Reported (DeXOR Table 2).** Geomean 14.86 bits on DeXOR's low-precision datasets vs Gorilla 46.82, Elf 23.18; DeXOR states Camel "works losslessly only in low-dp datasets" and loses accuracy or overflows on high-precision data.
- **Fit.** Not verifiable and, per DeXOR, not lossless in general. Its integer/fraction split is a weaker form of our decimal mantissa column.

### DeXOR (PVLDB 19(5) 2026)

- **Mechanism.** "Decimal XOR": converts each value to a scaled decimal and codes the longest common decimal prefix with the previous value plus the differing suffix, with error-tolerant rounding for binary-decimal conversion and an exception handler that codes the exponent difference adaptively for values that do not fit ([Lv et al., PVLDB 19(5) 2026](https://www.vldb.org/pvldb/vol19/p849-li.pdf); [arXiv 2601.00695](https://arxiv.org/abs/2601.00695)).
- **Reported.** 22 datasets: geomean 17.82 bits vs Gorilla 49.09, Chimp 51.18, Elf 29.72, Elf+ 27.31 (all datasets); 12.70 vs Camel 14.86 on low-precision ones; "15% higher compression ratio and 20% faster decompression" than the state of the art; compression speed within 5% of Chimp.
- **Lossless.** Yes. **Code.** [SuDIS-ZJU/DeXOR](https://github.com/SuDIS-ZJU/DeXOR), MIT.
- **Fit.** Low to medium. It is the best streaming per-value method, but its gain over the Elf family is exactly "work in decimal space with a predecessor", which our chunk-level decimal mantissa + delta already does. Its **adaptive exception handler** (values outside the common decimal form do not break the stream) is the same lesson as ALP's exceptions.

### BUFF (PVLDB 2021)

- **Mechanism.** For bounded-precision data with a known number of decimals, converts each value to a fixed-point integer offset from the column minimum, splits it into byte-wide sub-columns ("byte slices"), and evaluates filters directly on the slices with SIMD; an outlier variant stores out-of-range values sparsely ([Liu, Jiang, Paparrizos, Elmore, PVLDB 14(11) 2021](https://www.vldb.org/pvldb/vol14/p2586-liu.pdf)).
- **Reported.** Gorilla "performs poorly on most of our datasets"; BUFF's ratio is close to Sprintz and plain fixed-point, with large query speedups. FCBench notes that without precision information BUFF is effectively lossy ([FCBench, PVLDB 17(6) 2024](https://arxiv.org/abs/2312.10301)).
- **Lossless.** Only with respect to the declared decimal precision. **Code.** [Tranway1/buff](https://github.com/Tranway1/buff), MIT.
- **Fit.** None for size: we do not query compressed data, and our decimal mode finds the precision itself.

### Pseudodecimal Encoding (BtrBlocks, SIGMOD 2023)

- **Mechanism.** Each double becomes a pair (significant digits with sign, exponent), e.g. 3.25 → (+325, 2), found per value by searching for the shortest decimal that round-trips; values without one are exceptions; the two integer streams are then compressed by BtrBlocks' cascade (bit-packing, RLE, dictionary) chosen by sampling. Disabled for columns with more than 50% exceptions ([Kuschewski et al., BtrBlocks, SIGMOD 2023](https://www.cs.cit.tum.de/fileadmin/w00cfj/dis/papers/btrblocks.pdf)).
- **Reported.** Dictionary and Pseudodecimal give 95% and 20% improvements on doubles in BtrBlocks' Public BI evaluation; in ALP's Table 4 Pseudodecimal averages 31.4 bpv and compresses 251x slower than ALP because of the per-value search (ALP paper, Section 4).
- **Lossless.** Yes. **Code.** [maxi-k/btrblocks](https://github.com/maxi-k/btrblocks), MIT.
- **Fit.** Our decimal mode is the chunk-level version (one exponent per chunk). The per-value exponent is what lets a chunk with mixed precision stay decimal; ALP shows a per-vector exponent plus exceptions gets that benefit far more cheaply.

### ALP and ALP-RD (SIGMOD 2024, CWI)

- **Mechanism.** Per vector of 1,024 values, ALP picks an exponent e and a factor f (from a sample: 8 vectors x 32 values per row group, then 32 values per vector) and encodes `n = round(d * 10^e * 10^-f)`; decoding is `n * 10^f * 10^-e`. Values for which this does not round-trip are **exceptions**: stored raw with their position (80 bits each at vector size 1,024) and patched after decoding. The integers are frame-of-reference + bit-packed with FastLanes. The factor f strips trailing decimal zeros (1200.0 → 12). For "real doubles" that have no short decimal, ALP-RD cuts each value at a bit position p, codes the left part with a small dictionary (at most 8 entries, exceptions as 16-bit values) and bit-packs the right part raw ([Afroozeh, Kuffó, Boncz, SIGMOD 2024](https://ir.cwi.nl/pub/33334/33334.pdf); [cwida/ALP](https://github.com/cwida/ALP)).
- **Reported.** 30 datasets, bpv: ALP 21.7 average vs Gorilla 42.2, Chimp 37.7, Chimp128 28.7, Patas 35.5, Pseudodecimal 31.4, Elf 23.1, zstd 20.6; on the 13 time series ALP 16.4 vs zstd 17.2. ALP is 49% better than Gorilla on average. ALP compresses 8x faster than Patas; decoding is the fastest of all schemes. Using visible precision as the exponent round-trips only 82.5% of values on average, which is why e and f are searched. ALP is DuckDB's default float compression.
- **Lossless.** Yes, bit-exact. **Code.** MIT, C++ (Rust port: crate `alp`).
- **Fit for HSM.** ALP's decimal detection ≈ our decimal mode: both scale by a power of ten and keep the chunk only if values round-trip. Three differences are worth importing: (1) **exceptions** — our mode is all-or-nothing per chunk, so one non-decimal value (a computed EMA, a NaN, an artefact) pushes the whole chunk to XOR; (2) **the factor f** for mantissas with trailing zeros, which our delta coder pays for in bits; (3) ALP-RD's front-bit dictionary for non-decimal columns. ALP itself uses frame-of-reference, not delta, so on smooth time series our delta/delta-of-delta of mantissas should already do at least as well; ALP's time-series gain over zstd is small (16.4 vs 17.2 bpv).

### Pcodec / Pco (arXiv 2025)

- **Mechanism.** Per chunk, a **mode** splits numbers into latent integers: Classic (order-preserving bits), IntMult (`x = q*m + r`, m found from the GCD of differences in a sample — the paper's example is millisecond timestamps stored as microseconds), FloatMult (`x ≈ q*base` plus a small float correction, e.g. prices in multiples of 0.01), FloatQuant. Then **delta**: none, consecutive differences of order up to 7, or "lookback" (difference to a chosen earlier value, an LZ-like match at the number level). Then **binning**: latents are grouped into optimized bins and the bin index is entropy-coded with tANS, the offset inside the bin written raw ([Loncaric, Jeppesen, Zinberg, arXiv 2502.06112](https://arxiv.org/abs/2502.06112); [pcodec/pcodec](https://github.com/pcodec/pcodec)).
- **Reported.** 29–94% higher compression ratio than Blosc2, Parquet (delta or dictionary + zstd), SPDP, TurboPFor + zstd, Vortex and zstd on six real columnar datasets, with less compression time; decompression above 1 GiB/s per thread. No comparison with Gorilla or the XOR family.
- **Lossless.** Yes. **Code.** Rust, Apache-2.0; used by Zarr and CnosDB per the paper.
- **Fit for HSM.** High as a source of ideas, low as a dependency (native Rust; our codec is pure managed). Pco is the closest published design to ours (per-chunk mode + delta order), and it adds the two things we lack: **entropy coding of residuals with bins fitted to the chunk** (our bucket code is fixed) and **IntMult/FloatMult with a residual**, which tolerates values that are not exact multiples. A cheap experiment: export each column of the measured shard and compress it with the `pcodec` CLI to get a ceiling for our numeric columns.

### Parquet BYTE_STREAM_SPLIT, and shuffle + zstd

- **Mechanism.** K byte streams of length N for K-byte values: all first bytes, then all second bytes, and so on. It "does not reduce the size of the data but can lead to a significantly better compression ratio" when a compressor follows ([Parquet encodings](https://parquet.apache.org/docs/file-format/data-pages/encodings/)).
- **Reported.** FCBench, 33 datasets, original/compressed at 64 KB blocks: bit-shuffle + zstd 1.466, Chimp 1.315, Gorilla 1.095; bit-shuffle + zstd is best on observational data (FCBench Table 10 and Section 7). FCBench measures bit shuffle, not Parquet's byte split; the two are not interchangeable ahead of zstd, so the byte-split figure for our data has to be measured.
- **Fit.** Cheap candidate for the non-decimal doubles (Rate) where we now use XOR: our payload is zstd-compressed anyway, and our own ClickHouse test found plain zstd smaller than Gorilla and FPC on doubles. Expected gain is small because Rate mantissas are noise (see [HSM observation on Rate](#hsm-observation-rate-values)).

### Serf (SIGMOD 2025) — error-bounded, listed for completeness

- Streaming error-bounded compressor with two variants, Serf-Qt (quantization) and Serf-XOR, absolute or relative bound ([DOI 10.1145/3725353](https://doi.org/10.1145/3725353); [Spatio-Temporal-Lab/Serf](https://github.com/Spatio-Temporal-Lab/Serf), no licence). Paper not read; the README gives no numbers. Lossy, so it belongs to the [lossy section](#lossy-and-model-based-methods).

### Same-data comparison (ALP paper, Table 4)

Bits per value, lower is better, 64 = uncompressed ([ALP paper](https://ir.cwi.nl/pub/33334/33334.pdf)):

| Datasets | Gorilla | Chimp | Chimp128 | Patas | PDE | Elf | ALP | zstd |
|---|---|---|---|---|---|---|---|---|
| 13 time series, average | 48.1 | 42.6 | 24.5 | 30.9 | 30.0 | 18.2 | 16.4 | 17.2 |
| 17 non-time series, average | 37.7 | 34.0 | 31.8 | 39.0 | 32.5 | 26.9 | 25.7 | 23.3 |
| All 30, average | 42.2 | 37.7 | 28.7 | 35.5 | 31.4 | 23.1 | 21.7 | 20.6 |
| POI-lat (real doubles, up to 20 decimals) | 66.0 | 57.7 | 57.5 | 71.7 | 69.3 | 62.5 | 55.5 (ALP-RD) | 48.1 |
| Stocks-USA (fixed decimals) | 37.7 | 35.0 | 12.2 | 19.2 | 26.1 | 8.8 | 7.9 | 7.8 |

Read with our data in mind: on rounded decimals the decimal family (ALP, Elf) cuts Gorilla to between a third (13 time series) and a fifth (Stocks-USA); on real doubles (POI-lat) Gorilla, Patas and PDE are larger than raw, the rest are 10–25% under it, and zstd is best.

### Benchmarks and surveys

| Study | Scope | Main finding relevant to us |
|---|---|---|
| [FCBench, PVLDB 17(6) 2024](https://arxiv.org/abs/2312.10301) ([code](https://github.com/hipdac-lab/FCBench)) | 8 CPU + 5 GPU lossless float compressors, 33 datasets (HPC, time series, observation, database) | Byte/bit shuffling before a general compressor raises ratio; delta/Lorenzo predictors win on structured data, dictionary methods on repeated values; Gorilla is near the bottom (CR 1.10) |
| [Beyond Compression, PVLDB 18(11) 2025](https://www.vldb.org/pvldb/vol18/p4396-hishida.pdf) ([EBI, Rust, MIT](https://github.com/lemolatoon/ebi)) | Ratio, DB operators, ML queries for Gorilla, Chimp, Elf, ALP, BUFF, Sprintz, zstd and others | No single winner; Elf among the best ratios but slow decompression; ALP leads speed; zstd and ALP strong on non-time-series data |
| [Xiao et al., PVLDB 15(10) 2022](https://www.vldb.org/pvldb/vol15/p2148-song.pdf) (Apache IoTDB) | TS_2DIFF, Gorilla, Sprintz, RLE, RLBE, RAKE on generated and industrial data | Which encoding wins depends on value scale, delta mean/variance, repeats and monotonicity — supports choosing per chunk, as we do |

## Timestamps

Nothing published targets irregular, 100 ns timestamps; a search for 2024–2026 work on irregular timestamps found only vendor documentation (TimescaleDB, ClickHouse) that says delta-of-delta loses its advantage when spacing varies. What exists:

| Method | Mechanism | Fit for HSM |
|---|---|---|
| Gorilla delta-of-delta | Buckets of 7/9/12/32 bits with 1–4-bit prefixes; 96% of Facebook's timestamps in one bit ([Gorilla](https://www.vldb.org/pvldb/vol8/p1816-teller.pdf)) | Measured in the storage research with a Gorilla-style bucket code: 59.7 bits/row at 100 ns vs 21.1 for our variable-length delta-of-delta. The paper's buckets cap a timestamp at 36 bits, so that figure comes from a variant with a wider fallback (Prometheus falls back to 64 bits); either way it is rejected |
| TS_2DIFF (IoTDB) | Second difference, subtract the block minimum, bit-pack at the block's maximum width ([Xiao et al. 2022](https://www.vldb.org/pvldb/vol15/p2148-song.pdf)) | Per-block width instead of our per-value bucket prefix; one wide outlier widens the whole block |
| Parquet `DELTA_BINARY_PACKED` | Deltas in blocks of a multiple of 128 values, minimum delta subtracted, a bit width per miniblock of 32 values ([Parquet encodings](https://parquet.apache.org/docs/file-format/data-pages/encodings/)) | Same idea at finer grain (32 values); no second difference |
| BOS (ICDE 2025) | Bit-packing that stores upper **and** lower outliers separately so the remaining values use a narrow width; replaced bit-packing in Apache IoTDB and TsFile; ratio 2.75 → 3.25 when substituted into existing encoders ([Xiao, Guo, Song, ICDE 2025](https://sxsong.github.io/doc/25ICDE-BOS.pdf)) | Directly applicable to delta-of-delta residuals with jitter: most residuals small, a few large |
| Pcodec IntMult + delta + binning | Detects a common multiplier via GCDs (e.g. ms timestamps stored as µs), delta of any order, tANS over fitted bins ([Pcodec](https://arxiv.org/abs/2502.06112)) | IntMult helps only the 31% of `Time` values that are millisecond-aligned; the binning/entropy part helps every residual column |
| LogReducer / Denum "delta timestamps" | Timestamps in logs stored as differences to the previous one ([LogReducer, FAST 2021](https://yangwang83.github.io/papers/fast21-wei.pdf); [Denum, ASE 2024](https://arxiv.org/abs/2408.05760)) | Already our order-1 residual |

**Verdict.** Our `Time` costs 16–24 bits/row because sub-millisecond jitter is real entropy (69% of rows have a non-zero sub-millisecond part); no method removes that. What can still be won is the gap between our fixed bucket code (widths 0, 4, 8, 12, 16, 24, 32, 64 with a unary index of up to 7 bits) and the empirical entropy of the residuals. That gap is measurable without writing a codec: compute the per-chunk histogram entropy of the chosen residuals in `SealedHistoryBench` and compare with the bits written.

## Integer coders

| Method | Mechanism | Reported | Code | Fit for HSM |
|---|---|---|---|---|
| zigzag + LEB128 varint | Sign folded into the low bit, 7 data bits per byte ([Protocol Buffers encoding](https://protobuf.dev/programming-guides/encoding/)) | — | — | Already used (integer mode 1) |
| Simple-8b | A 4-bit selector per 64-bit word says how many equal-width integers fill the other 60 bits ([Anh and Moffat, Software: Practice and Experience 40(2) 2010, DOI 10.1002/spe.948](https://people.eng.unimelb.edu.au/ammoffat/abstracts/am10spe.html)); used for InfluxDB integers and timestamps ([influxdb pkg/encoding](https://pkg.go.dev/github.com/influxdata/influxdb/v2/pkg/encoding)) | Fast, word-aligned | many | Word-aligned middle ground between our varints and our bit stream; it does not beat entropy coding, and zstd then sees aligned words |
| Frame-of-reference / PFOR / BOS | Subtract a block minimum and bit-pack; patched variants store outliers aside (BOS above) | BOS: 2.75 → 3.25 in IoTDB | [apache/tsfile](https://github.com/apache/tsfile), Apache-2.0 | Candidate replacement for the per-value bucket prefix |
| Sprintz (IMWUT 2018) | Forecast each integer (delta, or FIRE — an online linear predictor), zigzag the error, bit-pack in tiny blocks of 8 with a per-block width header, run-length code all-zero blocks, optional Huffman; FIRE is reported to improve ratio "significantly" over delta coding ([Blalock et al., arXiv 1808.02515](https://arxiv.org/abs/1808.02515)) | Built for kilobyte-memory devices | [dblalock/sprintz](https://github.com/dblalock/sprintz), Apache-2.0 | FIRE is a learned order-1/order-2 mix; our per-chunk choice of order 0/1/2 already captures most of it for slowly drifting series |
| FastLanes (PVLDB 2023) | Interleaved bit-packing layout for a virtual 1024-bit register, plus a transposed tuple order, so FOR, DELTA, DICT and RLE decode with auto-vectorized scalar code (>40 values per cycle) ([Afroozeh and Boncz, PVLDB 16(9) 2023](https://www.vldb.org/pvldb/vol16/p2132-afroozeh.pdf)) | Speed, not ratio | [cwida/FastLanes](https://github.com/cwida/FastLanes), MIT | No: our decode is already 0.25 µs/row and size is the goal |
| LeCo (SIGMOD 2024) | Fits a regression model (linear or higher) per partition and stores bit-packed residuals; FOR, delta and RLE are special cases; partition size searched on a sample ([Liu, Zeng, Zhang, arXiv 2306.15374](https://arxiv.org/abs/2306.15374)) | Up to 91% better ratio than FOR and Elias-Fano, with random access | not checked | Low: aimed at random access; for us delta-of-delta is the linear model, and timestamps are jitter, not curve |
| Pcodec binning + tANS | See Pcodec above | 29–94% over Parquet/TurboPFor/zstd on 6 datasets | Apache-2.0 | The strongest residual coder found; costs a tANS implementation |

## Strings

Our String values are short (~30 chars), every value distinct, with digit runs and mostly decimals; a template-extraction prototype gave −21..−26% on top of today's dictionary + zstd. The literature on this shape of data is log compression, not database string compression.

| Method | Mechanism | Reported | Code | Fit for HSM |
|---|---|---|---|---|
| FSST (PVLDB 2020) | A static table of up to 255 symbols of 1–8 bytes; each symbol becomes a 1-byte code, code 255 escapes a literal byte; each string decodes on its own ([Boncz, Neumann, Leis, PVLDB 13(11) 2020](https://www.vldb.org/pvldb/vol13/p2649-boncz.pdf)) | Average 2.28x vs LZ4 1.70x on database text; LZ4 on the urls set falls from 2.73x at 64 KB blocks to 1.59x at 1 KB and 1.14x at 256 B | [cwida/fsst](https://github.com/cwida/fsst), MIT | Low: its point is random access to one string; we decode whole chunks and zstd over the chunk is stronger. Its small-block data is relevant though: sparse sensors produce tiny chunks where any LZ coder has nothing to learn from (see shortlist item 4) |
| CLP (OSDI 2021) | Each message → a **log type** (static text with placeholders), **dictionary variables** (ids, IPs) in a variable dictionary, and **non-dictionary variables** (integers, floats) encoded inline as 64-bit values; stored column-wise (timestamps, log-type ids, variables) per segment, then zstd ([Rodrigues, Luo, Yuan, OSDI 2021](https://www.usenix.org/system/files/osdi21-rodrigues.pdf)) | About 2x the ratio of gzip, better than zstd at its highest level | [y-scope/clp](https://github.com/y-scope/clp), Apache-2.0 | High: this is our template extraction plus **typed numeric variable columns** |
| LogReducer (FAST 2021) | Parser-based; adds delta timestamps, "correlation identification" (a variable expressed as a residual of another) and "elastic encoding" (numbers stored according to their size) ([Wei et al., FAST 2021](https://yangwang83.github.io/papers/fast21-wei.pdf)) | 1.19–5.30x the ratio of LZMA on 18 Alibaba Cloud and 16 public logs; elastic encoding alone ≥1.05x on 12 logs | — | High: numbers inside messages as integers, not digit text |
| LogShrink (ICSE 2024) | Longest-common-subsequence and entropy analysis finds the common and variable parts of messages, a clustering sampler speeds it up, variable parts stored column-wise, then a general compressor ([Li et al., arXiv 2309.09479](https://arxiv.org/abs/2309.09479)) | 16–356% higher ratio than 2 log-specific and 3 general compressors on 16 public logs | [IntelligentDDS/LogShrink](https://github.com/IntelligentDDS/LogShrink), MIT | Medium: template discovery without hand-written rules |
| Denum (ASE 2024) | Extracts every numeric token (numbers, numbers with separators, numeric variables), stores them by kind (incremental ones as differences), compresses the rest as text, then a general compressor ([Yu et al., arXiv 2408.05760](https://arxiv.org/abs/2408.05760)) | Up to 70% of the output of existing log compressors is numeric tokens; adding Denum's numeric module to existing compressors improves their ratio by 11.8% on average; 8.7–434.7% over baselines on 16 datasets | [gaiusyu/Denum](https://github.com/gaiusyu/Denum), Apache-2.0 | High: our messages are digit runs; the numeric module is the part to copy |
| BtrBlocks cascade | Dictionary, then FSST over the dictionary; scheme chosen by sampling ([BtrBlocks](https://www.cs.cit.tum.de/fileadmin/w00cfj/dis/papers/btrblocks.pdf)) | Dictionary 7x on strings, FSST on the dictionary +51% | MIT | Our dictionary is useless when every value is distinct; the cascade idea (dictionary of templates, not of values) is what CLP does |
| OpenZL (Meta, 2025) | Compression as a graph of small codecs over parsed, typed streams, with a trainer and one universal decoder ([Collet et al., arXiv 2510.03203](https://arxiv.org/abs/2510.03203)) | Beats xz's ratio at an order of magnitude faster compression on its benchmark datasets; e.g. 55% better than xz-9 on one census dataset | [facebook/openzl](https://github.com/facebook/openzl), licence not identified by GitHub | Confirms the direction (parse into typed streams, code each), but it is C with no managed port |

Newer log work found but not read: DeLog (arXiv 2601.15084, 2026) and LogFold (ICSE 2026, arXiv 2603.20618), which describes Denum as grouping numeric tokens by length and leading digit.

## Lossy and model-based methods

The owner decided on 2026-11-19 that values are not rounded in the first version. These methods matter only if that decision is revisited, per sensor.

| Method | Mechanism | Reported | Fit |
|---|---|---|---|
| Piecewise constant / linear approximation (PMC, Swing, Slide) | Replace runs of points by constant or linear segments within an error bound ε | Baselines in Sim-Piece | Need ε per sensor; timestamps still stored |
| Sim-Piece (PVLDB 2023), Mix-Piece (VLDB Journal 2024) | Build PLA segments with ε, then group segments with similar slopes and quantized intercepts so a group shares one slope ([Kitsios et al., PVLDB 16(8) 2023](https://www.vldb.org/pvldb/vol16/p1910-liakos.pdf); [Mix-Piece](https://cgi.di.uoa.gr/~p.liakos/files/mix-piece.pdf)) | More than twofold average improvement over PLA algorithms; the authors note that lossless methods now undercut PLA at small ε | Code [xkitsios/sim-piece](https://github.com/xkitsios/sim-piece), Apache-2.0. Our 3-significant-digit what-if already reaches 5.5 B/row with the existing codec |
| ModelarDB (PVLDB 2018) | Per segment, the best of several models (constant, linear, Gorilla) within an error bound, chosen online; groups of correlated series share models ([Jensen, Pedersen, Thomsen, PVLDB 11(11) 2018](http://www.vldb.org/pvldb/vol11/p1688-jensen.pdf)) | Paper not read | A system, not a codec; its "best model per segment" is our "best mode per chunk" with an error bound |
| Serf (SIGMOD 2025) | Error-bounded quantization or XOR (see above) | Not read | — |
| BUFF / Sprintz with a scale factor | Quantize to a declared decimal precision, then integer coding | See above | Equivalent to our "round to k decimals" what-if |

Measured in our own data ([storage research](sensor-history-storage.md)): rounding every double to 3 or 4 significant digits gives 5.5–5.7 B/row with the current codec, against 9.5 lossless. A model-based method would add value only below that, at the cost of an error bound per sensor.

## HSM observation: Rate values

Not from the literature, but it decides how much any float paper can do for 61% of our rows. The managed collector computes a Rate value as `sum / sec`, where `sec` is the elapsed time between samples measured with `Stopwatch` (`src/collector/HSMDataCollector/Sensors/InstantSensors/MonitoringCounterSensor.cs`, `GetValue`). The divisor is a monotonic-clock reading the server never sees, so the mantissa of a changed Rate value is effectively random: no lossless coder recovers it, and what is left is the 20–27% a general compressor takes off (28.9 → 23.1 bits/value measured; zstd 27% under Gorilla on POI-lat). The 28.9 bits/value we measure come mostly from the 55% of values equal to the previous one. Only three things change that: rounding (owner decision), sending `sum` and `sec` instead of the ratio (a collector and wire change), or accepting it. The native collector was not checked.

## Shortlist for #1802

Ranked by expected effect on production bytes per unit of work. "Expected effect" is a literature or measurement anchor, not a promise; each item names the measurement that should come first.

| # | Idea (source) | What changes in our codec | Expected effect | Cost | Measure first |
|---|---|---|---|---|---|
| 1 | **Typed template variables for String columns** (CLP; Denum numeric module; LogReducer elastic encoding) | String column v2: template dictionary + per-slot columns; integer-like slots go to an integer column (delta per slot), decimal-like slots to a decimal column, the rest to a string dictionary. Exactness needs a per-slot format descriptor (leading zeros, sign, digit count) | Strings are 65.5% of sealed bytes on SoftFx production. Template extraction alone measured −21..−26%; typed numeric slots come on top. Literature anchor: Denum's numeric module +11.8% ratio on average when added to other compressors | Medium–high: parser, lossless digit-string rules, new format version, golden files | Re-run the template prototype with numeric slots split out, on the SoftFx shard |
| 2 | **Exceptions inside the decimal mode** (ALP; also Pseudodecimal, DeXOR's exception handler) and ALP's **factor f** | Double mode 4: one exponent per chunk, positions + raw bits of values that do not round-trip; optional factor to strip common trailing zeros | Only chunks that today fall back to XOR/Gorilla because of a few non-decimal values gain, but those fall from ~30 to single-digit bits per value. ALP's exception cost: 80 bits each at 1,024 values | Low–medium | Count, per double column, chunks not in decimal mode and the share of their values that would round-trip with the chunk's best e |
| 3 | **Adaptive residual coding for integer columns** (Pcodec binning + tANS; BOS; Parquet miniblock widths) | Replace the fixed 8-bucket code by per-chunk bucket widths stored in the column header (cheap), or by bins + tANS (Pco) | Applies to `Time`, `ReceivingTime` and every integer column; timestamps are more than half of a row after values are encoded. Size of the gain = gap between bits written and residual entropy; unknown | Low (per-chunk bucket table) to medium (tANS) | In `SealedHistoryBench`, per chunk: bits written vs empirical entropy of the chosen residual series; also run the `pcodec` CLI on exported columns as a ceiling |
| 4 | **Shared zstd dictionary for small chunks** (FSST's block-size data; OpenZL training) | A trained dictionary per deployment (or per sensor family), referenced by id from the chunk header | Median sensor has 13 rows per week, so most chunks are tiny; FSST shows LZ ratio collapsing below ~1 KB. Unknown for us | Medium: dictionary storage, versioning and lifetime become part of the format | zstd with and without a trained dictionary over the real sealed chunks, grouped by chunk size |
| 5 | **Byte-stream-split mode for non-decimal doubles** (Parquet BYTE_STREAM_SPLIT; FCBench shuffle + zstd) | Double mode: repeat bitmap + the eight byte planes of changed values, left to the payload zstd | Small: Rate mantissas are noise (see Rate observation). FCBench: bit shuffle + zstd 1.47 vs Gorilla 1.10 on average across domains (bit, not byte, shuffle) | Low | Rate column bits/value: XOR bytes vs Gorilla vs BSS, each followed by the payload zstd |
| 6 | **Back-reference window** (Chimp128, TSXor, Pcodec lookback) | Integer/double residual "same as value k back" | Only if non-consecutive repeats are common | Medium | Share of values equal to one of the previous 128 but not to the previous one, per column |

Not recommended for #1802: the Elf family, Camel and DeXOR (streaming per-value schemes whose idea our chunk-level decimal mode already implements; Elf has no licence); BUFF, FastLanes, LeCo (query speed or random access, not size); FSST (random access we do not need); Pcodec and OpenZL as dependencies (native code in a pure-managed codec), though both are useful as offline ceilings; any lossy method until the owner revisits rounding.

## Could not verify

- Camel: the paper (ACM, 403) and code were not available; its description and the claim that it is lossless only on low-precision data come from the DeXOR paper.
- Serf, ModelarDB, Mix-Piece, TSXor and FPC papers were not read; their entries rest on repository READMEs, abstracts or author pages, as marked.
- Patas: described from the ALP paper; DuckDB's own page was not read.
- Licences: Elf, SElfStar, Serf, TSXor and FCBench repositories carry no licence file; OpenZL's licence is not identified by GitHub's API. LeCo's code location was not found.
- The native collector's Rate computation was not checked.
- No method was run on HSM data for this review; every "expected effect" above is to be measured.

## Sources

Papers read (PDF text extracted): Gorilla, Chimp, Elf, Elf+ (arXiv), Elf*/SElf* (arXiv), DeXOR, BUFF, BtrBlocks, ALP, Pcodec, FCBench, Beyond Compression, IoTDB encoding comparison, BOS, Sprintz, FastLanes, LeCo, FSST, CLP, LogReducer, LogShrink, Denum, OpenZL, Sim-Piece. Links are given inline at each claim. Our own facts: [sensor-history-storage.md](sensor-history-storage.md) and `aicontext/features/storage/sealed-history/feature.md` (branch `feature/1802-chunk-codec`).
