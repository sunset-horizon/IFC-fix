1、First, import the official plugin into the Unity project.(https://github.com/Chair-Intelligent-Technical-Design/IFC-Unity-Editor-Plugin)
2、Put this plugin in: Packages/com.art.ifc-material-fixer.Wait for Unity to compile.
3、Confirm that the menu includes: IFC Tools/Load IFC file with OBJ MTL materials
4、If not, restart Unity.
5、Click : IFC Tools > Load IFC file with OBJ MTL materials
6、Select your. ifc file.
7、Wait for the official plugin to automatically call IfoConvert.Unity will generate OBJ/MTL, usually in:Assets/Meshes，plugin automatically reads the corresponding. obj and. mtl.
Automatically create materials to:
Assets/Materials/IfcMtlColors
Automatically assign colors to imported IFC models.
8、If it has already been imported as a white mold using the official button，Select the root node of the IFC model in the Hierarchy.Click
Tools > IFC > Fix Selected IFC Materials Auto Find OBJ MTL
9、If OBJ/MTL cannot be found automatically, use the manual version:
Tools > IFC > Fix Selected IFC Materials From OBJ MTL
Then select one by one:
Corresponding. obj files;
The corresponding. mtl file.
