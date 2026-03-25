# the following is platformIO specific, and despite Import not being
# found by the VS Code LSP, this works. Consider it boilerplate.
Import("env")

print("htcw_buffers integration enabled")

env.Execute("python buffers_gen_c.py --buffers --out ./src --out_h ./include ./include/interface.h")
