function onCreate()
    Debug.Log("Cabin scene loaded.")
    Audio.Play("door_close_sound", 127, 64)
    local pos = Vec3.new(24, 8, -6)
    local rot = Vec3.new(10, -105, 0)
    setCameraPosition(pos, rot)
end
