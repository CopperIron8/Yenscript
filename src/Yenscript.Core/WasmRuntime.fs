namespace Yenscript.Core

module WasmRuntime =
    /// First-fit free list with private 16-byte headers and eight-byte alignment.
    /// Blocks are reused without splitting; memory is bounded by the module limit.
    let allocator heapStart =
        ("""
  (global $heap (mut i32) (i32.const HEAP_START))
  (global $free_head (mut i32) (i32.const 0))
  (func $alloc (export "alloc") (param $size i32) (result i32)
    (local $capacity i32) (local $node i32) (local $prev i32)
    (local $end i32) (local $pages i32)
    (if (i32.lt_s (local.get $size) (i32.const 0)) (then unreachable))
    (if (i32.gt_u (local.get $size) (i32.const 2147483600)) (then unreachable))
    (local.set $capacity (i32.and (i32.add (local.get $size) (i32.const 7)) (i32.const -8)))
    (if (i32.eqz (local.get $capacity)) (then (local.set $capacity (i32.const 8))))
    (local.set $node (global.get $free_head))
    (block $found
      (loop $search
        (br_if $found (i32.eqz (local.get $node)))
        (if (i32.ge_u (i32.load (local.get $node)) (local.get $capacity))
          (then
            (if (i32.eqz (local.get $prev))
              (then (global.set $free_head (i32.load offset=4 (local.get $node))))
              (else (i32.store offset=4 (local.get $prev) (i32.load offset=4 (local.get $node)))))
            (i32.store offset=8 (local.get $node) (local.get $size))
            (i32.store offset=12 (local.get $node) (i32.const 1))
            (return (i32.add (local.get $node) (i32.const 16)))))
        (local.set $prev (local.get $node))
        (local.set $node (i32.load offset=4 (local.get $node)))
        (br $search)))
    (local.set $node (global.get $heap))
    (local.set $end (i32.add (local.get $node) (i32.add (local.get $capacity) (i32.const 16))))
    (if (i32.lt_u (local.get $end) (local.get $node)) (then unreachable))
    (local.set $pages (i32.add (i32.shr_u (i32.sub (local.get $end) (i32.const 1)) (i32.const 16)) (i32.const 1)))
    (if (i32.gt_u (local.get $pages) (memory.size))
      (then
        (if (i32.eq (memory.grow (i32.sub (local.get $pages) (memory.size))) (i32.const -1)) (then unreachable))))
    (i32.store (local.get $node) (local.get $capacity))
    (i32.store offset=4 (local.get $node) (i32.const 0))
    (i32.store offset=8 (local.get $node) (local.get $size))
    (i32.store offset=12 (local.get $node) (i32.const 1))
    (global.set $heap (local.get $end))
    (i32.add (local.get $node) (i32.const 16))
  )
  (func (export "free") (param $ptr i32) (param $size i32) (local $node i32)
    (if (i32.lt_s (local.get $size) (i32.const 0)) (then unreachable))
    (if (i32.and (local.get $ptr) (i32.const 7)) (then unreachable))
    (if (i32.lt_u (local.get $ptr) (i32.const PAYLOAD_START)) (then unreachable))
    (if (i32.gt_u (local.get $ptr) (global.get $heap)) (then unreachable))
    (local.set $node (i32.sub (local.get $ptr) (i32.const 16)))
    (if (i32.ne (i32.load offset=12 (local.get $node)) (i32.const 1)) (then unreachable))
    (if (i32.ne (i32.load offset=8 (local.get $node)) (local.get $size)) (then unreachable))
    (i32.store offset=12 (local.get $node) (i32.const 0))
    (i32.store offset=4 (local.get $node) (global.get $free_head))
    (global.set $free_head (local.get $node))
  )
"""
         |> fun text -> text.Replace("HEAP_START", string heapStart).Replace("PAYLOAD_START", string (heapStart + 16)))
