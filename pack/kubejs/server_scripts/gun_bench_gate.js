// gun_bench_gate.js
//
// Gates firearm access:
// 1. The Gun Bench (scguns:gun_bench) is ENTIRELY uncraftable: guns are
//    already craftable via Create mechanical crafting, so the bench itself is
//    now find-only (world loot/structures) - owning one is a shortcut you
//    earn by exploring, not something you craft. Only the bench's own
//    crafting-table recipe is removed; every gun recipe is untouched.
// 2. Stone gun barrels cost 3 andesite alloy instead of 3 stone, pushing
//    early firearms behind Create's andesite age.
// 3. The (anthralite) gun barrel is now an UPGRADE of a stone gun barrel:
//    stone barrel in the middle, anthralite ingot on each diagonal end -
//    instead of being crafted from scratch with 3 anthralite.
//
// Reversible: delete this file and run /reload.

ServerEvents.recipes(event => {
  event.remove({ id: 'scguns:gun_bench' })

  event.replaceInput(
    { id: 'scguns:stone_gun_barrel' },
    'minecraft:stone',
    'create:andesite_alloy'
  )

  event.remove({ id: 'scguns:gun_barrel' })
  event.shaped('scguns:gun_barrel', [
    '  Q',
    ' B ',
    'Q  '
  ], {
    Q: 'scguns:anthralite_ingot',
    B: 'scguns:stone_gun_barrel'
  }).id('kubejs:gun_barrel_upgrade')
})
