function onCreate()
    Debug.Log("Cabin scene loaded.")
    local pos = Vec3.new(24, 8, -6)
    local rot = Vec3.new(10, -105, 0)
    setCameraPosition(pos, rot)
end
