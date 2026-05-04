function onTriggerEnter()
    local currentScene = Scene.GetIndex()

    if currentScene == 0 then
        Scene.Load(1)
    end
    if currentScene == 1 then -- Cabin
        Scene.Load(0)
    end
end
