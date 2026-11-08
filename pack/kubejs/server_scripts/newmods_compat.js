// Cross-mod compat for the Aug 2026 new-mod batch (spawn, more_critters).
// 1) Farmer's Delight cutting-board recipes for spawn's whole fish.
// 2) Easier propeller recipes using more_critters Avoider Tails
//    (Avoiders swim with a propeller tail - the drop makes propellers cheap).

ServerEvents.recipes(event => {
    // --- Farmer's Delight cutting: whole fish -> slices (mirrors FD's salmon) ---
    const cutFish = (input, output, count) => {
        event.custom({
            type: 'farmersdelight:cutting',
            ingredients: [{ item: input }],
            result: [
                { item: { count: count, id: output } },
                { item: { count: 1, id: 'minecraft:bone_meal' } }
            ],
            tool: [
                { type: 'farmersdelight:item_ability', action: 'knife_dig' },
                { tag: 'c:tools/knife' }
            ]
        }).id('newmods_compat:cutting/' + output.replace(/.*:/, ''))
    }
    cutFish('spawn:herring', 'spawn:herring_slice', 2)
    cutFish('spawn:bluefish', 'spawn:bluefish_slice', 2)
    cutFish('spawn:tuna_chunk', 'spawn:tuna_slice', 2)

    // --- Avoider Tail propeller recipes ---
    event.shapeless('2x immersive_aircraft:propeller',
        ['more_critters:avoider_tail', 'more_critters:avoider_tail', '#c:ingots/iron'])
        .id('newmods_compat:propeller_from_avoider_tails')
    event.shapeless('create:propeller',
        ['more_critters:avoider_tail', 'more_critters:avoider_tail', 'create:andesite_alloy'])
        .id('newmods_compat:create_propeller_from_avoider_tails')
    event.shapeless('create_submarine:submarine_propeller',
        ['more_critters:avoider_tail', 'more_critters:avoider_tail', '#c:ingots/copper'])
        .id('newmods_compat:submarine_propeller_from_avoider_tails')
})
