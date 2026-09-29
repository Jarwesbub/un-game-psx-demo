local boundaryData = { [0] = { min = { x = -13, z = -18 }, max = { x = 12, z = -3 } }, }

function onCreate()
    Debug.Log("Cabin scene loaded.")
    Audio.Play("door_close", 127, 64)
    local pos = Vec3.new(24, 8, -6)
    local rot = Vec3.new(10, -105, 0)
    setCameraPosition(pos, rot)
    setGlobalPlayerBoundaries(boundaryData[0])
    -- Play background music.
    Audio.PlayCDDA(4)
end
