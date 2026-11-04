**What this changes**

**How you checked it** (a log line, a frame rate before and after, a screenshot)

**What you didn't test**

- [ ] No game files and nothing built from them: no ISO, EBOOT, translated code, dumps or `sonic06.exe`
- [ ] Toolkit changes are regenerated patch files (`tools/setup_ps3recomp.ps1 -Export`), not edits to `work/`
- [ ] Any temporary hooks were restored (`tools/re/hook.py restore`)
