ServerEvents.recipes(event => {
event.shapeless('drivebywire:wire',['tfmg:copper_wire'])
})

ServerEvents.recipes(event => {
event.shapeless('drivebywire:controller_hub',['create:brass_casing', "create:linked_controller"])
})

ServerEvents.recipes(event => {
event.shapeless('drivebywire:tweaked_controller_hub',['create:brass_casing', "create_tweaked_controllers:tweaked_linked_controller"])
})

ServerEvents.recipes(event => {
event.shapeless('drivebywire:backup_block',['create:railway_casing', "drivebywire:controller_hub"])
})

ServerEvents.recipes(event => {
event.shaped('drivebywire:wire_cutter',
    [
        'A A',
        ' A ',
        'B B'
    ],
    {
        A: 'create:iron_sheet',
        B: 'create:brass_sheet'
    }

)
})