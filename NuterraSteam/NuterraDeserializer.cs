﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CustomModules
{
	public static class NuterraDeserializer
	{
		private static Type kTypeShader = typeof(Shader);
		private static Type kTypeUnityObject = typeof(UnityEngine.Object);
		private static Type kTypeComponent = typeof(UnityEngine.Component);
		private static Type kTypeGameObject = typeof(UnityEngine.GameObject);
		private static Type kTypeTransform = typeof(UnityEngine.Transform);
		private static Type kTypeJToken = typeof(JToken);

		internal static string DeserializingBlock = "UNKNOWN";
		internal static ModContents DeserializingMod = null;

		// TTQMM Ref: GameObjectJSON.ApplyValues(object instance, Type instanceType, JObject json, string Spacing)
		// TTQMM Ref: GameObjectJSON.ApplyValue(object instance, Type instanceType, JProperty jsonProperty, string Spacing)
		// I've embedded these two functions
		public static object DeserializeJSONObject(object target, Type targetType, JObject jObject)
		{
			// Let's get reflective!
			foreach (JProperty jProperty in jObject.Properties())
			{
				NuterraMod.logger.Trace($" Attempting to deserialize {targetType.ToString()}.{jProperty.Name}");
				BindingFlags bind = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				try
				{
					// Parse our string
					string[] split = jProperty.Name.Split('|');
					if (split.Length == 0)
						continue;

					// See if we have a prefix |
					string name = split[0];
					bool wipe = false;
					bool instantiate = false;
					if (split.Length == 2)
					{
						wipe = split[0] == "Wipe";
						instantiate = split[0] == "Instantiate";
						name = split[1];
					}

					FieldInfo fieldInfo = targetType.GetField(name, bind);
					PropertyInfo propertyInfo = targetType.GetProperty(name, bind);

					MemberInfo memberInfo = null;
					Type memberType = null;
					if (propertyInfo != null)
					{
						memberType = propertyInfo.PropertyType;
						memberInfo = propertyInfo;
						
					}
					else if(fieldInfo != null)
					{
						memberType = fieldInfo.FieldType;
						memberInfo = fieldInfo;
					}
					else
					{
						NuterraMod.logger.Error($" Property '{name}' does not exist in type '{targetType}'");
						continue;
					}

					if (jProperty.Value != null)
                    {
						bool pass = false;
						switch (memberInfo.MemberType)
                        {
							case MemberTypes.Event:
								{
									NuterraMod.logger.Error($" Trying to assign value to a Event");
									break;
								}
							case MemberTypes.Constructor:
                                {
									NuterraMod.logger.Error($" Trying to assign value to a Constructor");
									break;
                                }
							case MemberTypes.Method:
                                {
									NuterraMod.logger.Error($" Trying to assign value to a Method");
									break;
                                }
							case MemberTypes.TypeInfo:
                                {
									NuterraMod.logger.Error($" Trying to assign value to a Type");
									break;
                                }
							case MemberTypes.NestedType:
                                {
									NuterraMod.logger.Error($" Trying to assign value to a NestedType");
									break;
                                }
							case MemberTypes.Custom:
                                {
									NuterraMod.logger.Error($" Trying to assign value to an custom MemberInfo");
									break;
                                }
							default:
                                {
									pass = true;
									break;
                                }
                        }
						if (!pass)
                        {
							throw new Exception("Attempting to assign a value to an invalid member type");
                        }
						else
                        {
							Type propertyType;
							if (propertyInfo != null)
                            {
								propertyType = propertyInfo.PropertyType;
                            }
							else
                            {
								propertyType = fieldInfo.FieldType;
                            }

							if (typeof(Delegate).IsAssignableFrom(propertyType))
                            {
								throw new Exception("Attempting to assign a value to a delegate");
                            }
						}
                    }

					bool isTransformOrGO = typeof(Transform).IsAssignableFrom(memberType) || typeof(GameObject).IsAssignableFrom(memberType);
					bool isIterable = !isTransformOrGO && (memberType.IsArray || (memberType.IsGenericType && typeof(IList).IsAssignableFrom(memberType)));
					bool isDictionary = !isTransformOrGO && memberType.IsGenericType && typeof(IDictionary).IsAssignableFrom(memberType);
					NuterraMod.logger.Trace($"Property is of type {memberType.FullName.ToString()}, iterable: {isIterable}, dictionary: {isDictionary}");

					// Switch on the type of JSON we are provided with
					switch (jProperty.Value.Type)
					{
						case JTokenType.Object:
						{
							// Handle objects in our large function, as they can mean new GameObjects, new Components, instantiators, duplicators, all sorts...
							if (isDictionary || !isIterable) {
								if (instantiate)
                                {
									NuterraMod.logger.Debug($"Attempting to deserialize instantiated object into field {name}");
                                }
								else
                                {
									NuterraMod.logger.Debug($"Attempting to deserialize object into field {name}");
                                }
								JObject jChild = jProperty.Value as JObject;
								SetJSONObject(jChild, target, wipe, instantiate, fieldInfo, propertyInfo, propertyInfo == null);
							}
							else if (memberType.IsValueType)
							{
								// is a struct
								NuterraMod.logger.Debug($"Detected {memberType} is Struct");
								JObject jChild = jProperty.Value as JObject;
								SetJSONObject(jChild, target, wipe, instantiate, fieldInfo, propertyInfo, propertyInfo == null);
							}
							else
                            {
								throw new Exception("Trying to deserialize an object into an iterable type!");
                            }
							break;
						}
						case JTokenType.Array:
						{
							// Handle arrays
							if (isIterable)
							{
								NuterraMod.logger.Debug($"Attempting to deserialize array into field {name}");
								JArray jArray = jProperty.Value as JArray;
								object sourceArray = null;
								if (!wipe)
									sourceArray = memberInfo.GetValueOfField(target);

								// Helper function to populate the array contents
								object newArray = MakeJSONArray(sourceArray, memberInfo.GetFieldType(), jArray, wipe);

								// Then set it back to the field / property
								memberInfo.SetValueOfField(target, newArray);
							}
							else
                            {
								throw new Exception("Trying to deserialize an array into a NON-iterable type!");
							}

							break;
						}
						default:
						{
							// The leaf node, parse the value into place
							if (!isIterable && !isDictionary) {
								if (jProperty.Value is JValue jValue)
								{
									NuterraMod.logger.Debug($"Attempting to deserialize value {jValue.Value.ToString()} into field {name}");
									DeserializeValueIntoTarget(target, memberInfo, jValue);
								}
								else
                                {
									NuterraMod.logger.Error($"Attempting to deserialize NON-VALUE {jProperty.Value.ToString()} into field {name}");
								}
							}
							else if (jProperty.Value is JValue jValue && jValue.Value == null)
							{
								NuterraMod.logger.Debug($"Attempting to deserialize value {jValue.Value.ToString()} into field {name}");
								DeserializeValueIntoTarget(target, memberInfo, jValue);
							}
							else
                            {
								throw new Exception("Attempting to deserialize a value into a Dictionary or Iterable");
                            }
							break;
						}
					}
				}
				catch (Exception e)
				{
					NuterraMod.logger.Error($" Failed to deserialize Json object:\n{e.ToString()}");
				}
			}

			return target;
		}

		// TTQMM Ref: JsonToGameObject.CreateGameObject(JObject json, GameObject GameObjectToPopulate = null, string Spacing = "", Transform searchParent = null)
		public static GameObject DeserializeIntoGameObject(JObject jObject, GameObject target)
		{
			if (target == null)
				target = new GameObject("New Deserialized Object");
			NuterraMod.logger.Trace($"Deserializing GameObject {target.name}");
			PushSearchTransform(target.transform);
			GameObject result = DeserializeIntoGameObject_Internal(jObject, target);
			NuterraMod.logger.Debug($"Finished deserializing GameObject {target.name}");
			PopSearchTransform();
			return result;
		}

		// TTQMM Ref: JsonToGameObject.CreateGameObject_Internal(JObject json, GameObject GameObjectToPopulate, string Spacing, Component instantiated = null, Type instantiatedType = null)
		private static GameObject DeserializeIntoGameObject_Internal(JObject jObject, GameObject target)
		{
			// Ensure we have a target object
			if (target == null)
			{
				target = new GameObject("Deserialized Object");
			}

			// Then read each JSON property and act accordingly
			foreach (JProperty jProperty in jObject.Properties())
			{
				string[] split = jProperty.Name.Split('|');

				bool Duplicate = jProperty.Name.StartsWith("Duplicate");
				bool Reference = jProperty.Name.StartsWith("Reference");
				bool IsGameObject = jProperty.Name.StartsWith("GameObject") || jProperty.Name.StartsWith("UnityEngine.GameObject");

				if (Duplicate || Reference || IsGameObject) //
				{
					GameObject childObject = null;
					string name = "Object child";
					int GetCustomName = jProperty.Name.LastIndexOf('|');
					if (GetCustomName != -1)
					{
						name = jProperty.Name.Substring(GetCustomName + 1);
					}

					if (Reference) // Copy a child object or component from another prefab
					{
						NuterraMod.logger.Debug($" Deserializing Reference {name}");
						if (TTReferences.GetReferenceFromBlockResource(name, out object reference))
						{
							NuterraMod.logger.Debug($" Found reference {reference}");
							if (reference is GameObject || reference is Transform)
							{
								// If the reference was to a GameObject or a Transform, then we just want to copy that whole object
								GameObject referenceObject = reference is GameObject ? (GameObject)reference : ((Transform)reference).gameObject;

								childObject = GameObject.Instantiate(referenceObject);
								string newName = referenceObject.name;
								int count = 1;
								while (target.transform.Find(newName))
								{
									newName = $"{name}_{++count}";
								}
								childObject.name = newName;
								childObject.transform.parent = target.transform;
								childObject.transform.localPosition = referenceObject.transform.localPosition;
								childObject.transform.localRotation = referenceObject.transform.localRotation;
								childObject.transform.localScale = referenceObject.transform.localScale;
								NuterraMod.logger.Debug($" Instantiated reference {childObject} as child of {target}");
							}
							else if (reference is Component)
							{
								// If we referenced a Component, we want to place a copy of that component on our target
								// However, if we already have a Component of the same type, we most likely want to override its values. 
								// This functionality is as per TTQMM
								Type type = reference.GetType();
								Component existingComponent = target.GetComponent(type);
								if (existingComponent == null)
								{
									NuterraMod.logger.Warn($" Could not find Component of type {type} - creating one now for shallow copy");
									existingComponent = target.AddComponent(type);
								}

								// Copy the reference and then deserialize our JSON into it
								ShallowCopy(type, reference, existingComponent, false);
								DeserializeJSONObject(existingComponent, type, jProperty.Value as JObject);
								continue;
							}
							else
							{
								NuterraMod.logger.Error($" Unknown object {reference} found as reference");
								continue;
							}
						}
						else
						{
							NuterraMod.logger.Error($" Could not find reference for {name} in deserialization");
							continue;
						}
					}
					else // Copy a child object from this prefab
					{
						NuterraMod.logger.Debug($" Deserializing gameObject {name}");
						if (Duplicate && (name.Contains('/') || name.Contains('.')))
						{
							object foundObject = GetCurrentSearchTransform().RecursiveFindWithProperties(name, fallback: GetRootSearchTransform());
							if (foundObject != null)
							{
								if (foundObject is Component foundComponent)
								{
									childObject = foundComponent.gameObject;
								}
								else if (foundObject is GameObject foundGameObject)
								{
									childObject = foundGameObject;
								}
								NuterraMod.logger.Debug($"Found object {childObject} under {childObject.transform.parent}");
							}
							else
                            {
								NuterraMod.logger.Trace($" Failed to find object with name {name}");
							}
						}
						if (childObject == null)
						{
							childObject = target.transform.Find(name)?.gameObject;
							if (childObject)
							{
								NuterraMod.logger.Debug($"Found object {childObject} with parent {childObject.transform.parent} under {target}");
							}
							else
                            {
								NuterraMod.logger.Debug($"Failed to find object {childObject} under {target}");
                            }
						}
					}

					// Fallback, just make an empty object
					if (childObject == null)
					{
						if (jProperty.Value.Type == JTokenType.Null)
						{
							NuterraMod.logger.Warn($" Deserializing failed to find {name} to delete");
							continue;
						}
						else
						{
							NuterraMod.logger.Debug($" Creating new GO with name {name} under GO {target.name}");
							childObject = new GameObject(name);
							childObject.transform.parent = target.transform;
						}
					}
					else
					{
						// If we've got no JSON data, that means we want to delete this target
						if(jProperty.Value.Type == JTokenType.Null)
						{
							NuterraMod.logger.Debug($" Deleting gameObject {childObject}");
							GameObject.DestroyImmediate(childObject);
							childObject = null;
							continue;
						}

						// We're duplicating this
						if (Duplicate)
						{
							GameObject original = childObject;
							childObject = GameObject.Instantiate(original);
							name = name.Substring(name.LastIndexOfAny(new char[] { '/', '.' }) + 1);
							string newName = $"{name}_copy";
							int count = 1;
							while (target.transform.Find(newName))
							{
								newName = $"{name}_copy_{(++count)}";
							}
							childObject.name = newName;
							childObject.transform.parent = target.transform;
							childObject.transform.SetAsLastSibling();
						}
					}

					NuterraMod.logger.Trace($" Deserializing jProp {jProperty.Name} --- {jProperty.Value}");
					// Switch back to DeserializeIntoGameObject so this will also update the child hierarchy
					DeserializeIntoGameObject((JObject)jProperty.Value, childObject);
				}
				else
                {
					NuterraMod.logger.Debug($" Deserializer adding component {split[0]} to gameObject {target}");

					// Format will be "{ComponentType} {Index}" where the index specifies the child index if there are multiple targets
					string typeNameAndIndex = split[0];
					string typeName = typeNameAndIndex.Split(' ')[0];
					Type type = TTReferences.GetType(typeName);

					if (type != null)
					{
						NuterraMod.logger.Trace($" Deserializer ready to find or create instance of type {type} on gameObject {target}");

						// See if we have an existing component
						Component component = target.GetComponentWithIndex(typeNameAndIndex);

						// A null JSON token means we should delete this object
						if (jProperty.Value.Type == JTokenType.Null)
						{
							if (component != null)
								Component.DestroyImmediate(component);
							else
								NuterraMod.logger.Error($" Could not find component of type {typeNameAndIndex} to destroy");
						}
						else // We have some data, let's process it
						{
							// If we couldn't find the component, make a new one
							if (component == null)
							{
								NuterraMod.logger.Warn($" Failed to find component {typeNameAndIndex} on target - making one now");
								component = target.gameObject.AddComponent(type);
							}
							else
							{
								NuterraMod.logger.Trace($" Component found");
							}

							// If we still can't find one, get it. This should like never happen, right?
							if (component == null)
							{
								NuterraMod.logger.Error($" Failed to find {typeNameAndIndex}, failed to AddComponent, but trying GetComponent");
								component = target.gameObject.GetComponent(type);
							}

							// If we still don't have one, exit
							if (component == null)
							{
								NuterraMod.logger.Error($" Could not find component {typeNameAndIndex}");
								continue;
							}

							// Now deserialize the JSON into the new Component
							NuterraMod.logger.Trace($" Preparing deserialization of property {jProperty.Name}");
							DeserializeJSONObject(component, type, jProperty.Value as JObject);
						}

						NuterraMod.logger.Trace($" Processing complete for type {type}");
						if (type == typeof(UnityEngine.Transform))
						{
							UnityEngine.Transform transform = component as Transform;
							StringBuilder sb = new StringBuilder("{\n");
							sb.Append($"\t\"localPosition\": {{\"x\": {transform.localPosition.x}, \"y\": {transform.localPosition.y}, \"z\": {transform.localPosition.z}}},\n");
							sb.Append($"\t\"localEulerAngles\": {{\"x\": {transform.localEulerAngles.x}, \"y\": {transform.localEulerAngles.y}, \"z\": {transform.localEulerAngles.z}}},\n");
							sb.Append($"\t\"localScale\": {{\"x\": {transform.localScale.x}, \"y\": {transform.localScale.y}, \"z\": {transform.localScale.z}}}\n");
							sb.Append("}");
							NuterraMod.logger.Trace($" Deserialized transform as: {sb.ToString()}");

						}
					}
					else
					{
						NuterraMod.logger.Error($" Could not find type {typeNameAndIndex}");
					}
				}
			}
			return target;
		}

		// Check if there's a convertor defined from type 1 to type 2
		private static bool CanConvert(Type fromType, Type toType, out UnaryExpression convertor)
		{
			try
			{
				// Throws an exception if there is no conversion from fromType to toType
				convertor = Expression.Convert(Expression.Parameter(fromType, null), toType);
				return true;
			}
			catch
			{
				convertor = null;
				return false;
			}
		}

		// Try to find and execute a convertor from the JSON value type to the Property type
		private static bool TryConvert(Type valueType, Type targetType, object value, out object converted)
		{
			if (CanConvert(valueType, targetType, out UnaryExpression convertor))
			{
				NuterraMod.logger.Debug($"Convertor found from {valueType} => {targetType}");
				if (convertor.Method != null)
				{
					NuterraMod.logger.Debug($"Convertor has method defined");
					converted = convertor.Method.Invoke(null, new object[] { value });
					return true;
				}
				else
				{
					NuterraMod.logger.Debug($"Trying dynamic conversion");
					converted = Convert.ChangeType(value, targetType);
					return true;
				}
			}
			converted = null;
			return false;
		}

		// TTQMM Ref: JsonToGameObject.SetJSONValue(JValue jValue, JProperty jsonProperty, object _instance, bool UseField, FieldInfo tField = null, PropertyInfo tProp = null)
		private static void DeserializeValueIntoTarget(object target, MemberInfo member, JValue jValue)
		{
			if (member is FieldInfo field)
			{
				object converted = null;
				bool successfulConversion = false;
				// Try invoking setter directly
				try
				{
					Type valueType = jValue.Value.GetType();
					Type fieldType = field.FieldType;
					// If we're converting to a system defined namespace, use the builtin
					if (!fieldType.Namespace.StartsWith("System") && TryConvert(valueType, fieldType, jValue.Value, out converted))
					{
						successfulConversion = true;
					}
				}
				catch (Exception e)
				{
					NuterraMod.logger.Warn($"Convertor failed");
					NuterraMod.logger.Warn(e.ToString());
				}
				if (!successfulConversion)
				{
					converted = DeserializeValue(jValue, field.FieldType);
				}
				member.SetValueOfField(target, converted);
			}
			else if (member is PropertyInfo property)
            {
				if (property.CanWrite)
				{
					MethodInfo setter = property.GetSetMethod();
					object instance = setter.IsStatic ? null : target;

					object converted = null;
					bool successfulConversion = false;
					// Try using convertor first
					try
					{
						Type valueType = jValue.Value.GetType();
						Type propertyType = property.PropertyType;
						// If we're converting to a system defined namespace, use the builtin
						if (!propertyType.Namespace.StartsWith("System") && TryConvert(valueType, propertyType, jValue.Value, out converted))
						{
							successfulConversion = true;
						}
					}
					catch (Exception e)
					{
						NuterraMod.logger.Warn($"Convertor failed");
						NuterraMod.logger.Warn(e.ToString());
					}
					if (!successfulConversion)
					{
						converted = DeserializeValue(jValue, property.PropertyType);
					}
                    setter.Invoke(instance, new object[] { converted });
                }
				else
                {
					if (property.CanRead)
					{
						NuterraMod.logger.Error($"Property {property.DeclaringType}.{property.ToString()}: {property.PropertyType} is NOT WRITEABLE");
					}
                    else
                    {
                        NuterraMod.logger.Error($"Property {property.DeclaringType}.{property.ToString()}: {property.PropertyType} is NEITHER READABLE NOR WRITEABLE?");
                    }
                }
            }
			else
            {
				NuterraMod.logger.Error($"INVALID MemberInfo {member.DeclaringType}.{member.ToString()}");
			}
		}

		private static Dictionary<Type, ConstructorInfo[]> CachedTypeConstructors = new Dictionary<Type, ConstructorInfo[]>();
		private static ConstructorInfo GetConstructorForValue(Type type, Type parameterType)
        {
			if (!CachedTypeConstructors.TryGetValue(type, out ConstructorInfo[] constructors))
			{
				constructors = new ConstructorInfo[] { };
				if (type != typeof(Material))
				{
					constructors = type.GetConstructors();
				}
				CachedTypeConstructors.Add(type, constructors);
			}
			ConstructorInfo typeConstructor = null;
			try
			{
				typeConstructor = constructors.First((ConstructorInfo info) =>
				{
					ParameterInfo[] parameters = info.GetParameters();
					return parameters.Length == 1 && parameters[0].ParameterType == parameterType;
				});
			}
			catch (Exception e)
            {
				NuterraMod.logger.Warn($"No constructor found");
			}
			return typeConstructor;
		}
		private static bool TryConstructor(ConstructorInfo constructor, object input, out object result)
        {
			try
			{
				if (constructor != null)
				{
					result = constructor.Invoke(new object[] { input });
					NuterraMod.logger.Debug($"Constructor {constructor.ToString()} worked");
					return true;
				}
				else
				{
					result = null;
					return false;
                }
            }
			catch (Exception e)
            {
				NuterraMod.logger.Warn($"Constructor {constructor.ToString()} failed");
				NuterraMod.logger.Warn(e.ToString());
				result = null;
				return false;
			}
        }
		private static object DeserializeValue(JValue jValue, Type type)
		{
			try // Try transforming to the target type
			{
				NuterraMod.logger.Debug($"Trying to convert value {jValue.Value} to type {type}");
				return jValue.ToObject(type);
			}
			catch // If we failed, we can try interpreting the jValue as a reference string
			{
				NuterraMod.logger.Warn($"Cast to {type} failed, trying constructors if possible");
				string value = null;
				object result;
				try
                {
					value = jValue.ToString();
				}
				catch (Exception e)
                {
					NuterraMod.logger.Error($"FAILED to cast to string");
					NuterraMod.logger.Error(e);
                }

				ConstructorInfo constructor;
				switch(jValue.Type)
                {
                    case JTokenType.Integer:
						int intValue = jValue.ToObject<int>();
						constructor = GetConstructorForValue(type, typeof(int));
						if (TryConstructor(constructor, intValue, out result))
                        {
							return result;
                        }
						goto case JTokenType.Float;
					case JTokenType.Bytes:
						byte byteValue = jValue.ToObject<byte>();
						constructor = GetConstructorForValue(type, typeof(byte));
						if (TryConstructor(constructor, byteValue, out result))
						{
							return result;
						}
						goto case JTokenType.Float;
					case JTokenType.Float:
						float floatValue = jValue.ToObject<float>();
						constructor = GetConstructorForValue(type, typeof(float));
						if (TryConstructor(constructor, floatValue, out result))
						{
							return result;
						}
						goto case JTokenType.String;
					case JTokenType.Boolean:
						bool boolValue = jValue.ToObject<bool>();
						constructor = GetConstructorForValue(type, typeof(bool));
						if (TryConstructor(constructor, boolValue, out result))
						{
							return result;
						}
						goto case JTokenType.String;
					case JTokenType.Guid:
						Guid guid = jValue.ToObject<Guid>();
						constructor = GetConstructorForValue(type, typeof(Guid));
						if (TryConstructor(constructor, guid, out result))
						{
							return result;
						}
						goto case JTokenType.String;
					case JTokenType.Uri:
						Uri uri = jValue.ToObject<Uri>();
						constructor = GetConstructorForValue(type, typeof(Uri));
						if (TryConstructor(constructor, uri, out result))
						{
							return result;
						}
						goto case JTokenType.String;
					case JTokenType.TimeSpan:
						TimeSpan timeSpan = jValue.ToObject<TimeSpan>();
						constructor = GetConstructorForValue(type, typeof(TimeSpan));
						if (TryConstructor(constructor, timeSpan, out result))
						{
							return result;
						}
						goto case JTokenType.String;
					case JTokenType.String:
						{
							string referenceString = value;
							int refIndex = referenceString.IndexOf('|');
							bool isTransform = typeof(Transform) == type || typeof(GameObject) == type;
							if (!isTransform && (refIndex == -1 || (referenceString.Contains("/") && referenceString.EndsWith("."))))
							{
								// Only try constructor if we know for sure is not a reference
								constructor = GetConstructorForValue(type, typeof(string));
								if (TryConstructor(constructor, value, out result))
								{
									return result;
								}
							}

							NuterraMod.logger.Debug($"Constructors all failed, treating as string reference");
							// Trim anything before the |
							string targetName = referenceString.Substring(refIndex + 1);
							// try to deserialize the value reference
							return DeserializeValueReference(targetName, referenceString, type);
						}
				}
				NuterraMod.logger.Error($"Value deserialization failed");
				return null;
			}
		}

		// TTQMM Ref: JsonToGameObject.GetValueFromString
		// This function is for setting a value based on a reference
		public static object DeserializeValueReference(string search, string searchFull, Type outType)
		{
			if (searchFull.StartsWith("Reference"))
			{
				NuterraMod.logger.Trace($" Detected value {search} as Reference");
				if (TTReferences.GetReferenceFromBlockResource(search, out var result)) // Get value from a block in the game
					return result;
			}
			else
			{
				if (!typeof(Transform).IsAssignableFrom(outType) && !typeof(GameObject).IsAssignableFrom(outType) && TTReferences.TryFind(search, DeserializingMod, outType, out object result))
				{
					NuterraMod.logger.Trace($" value has been retrieved for {search}");
					return result; // Get value from a value in the user database
				}

				NuterraMod.logger.Trace($" Attempting to search the local transform tree for {search}");
				try
				{
					// Last fallback, we try searching our current working tree
					var recursive = GetCurrentSearchTransform().RecursiveFindWithProperties(searchFull, GetRootSearchTransform());
					if (recursive != null)
						return recursive; // Get value from this block
					else
						NuterraMod.logger.Warn($" FAILED to find {search} under current block");
				}
				catch (Exception e)
				{
					NuterraMod.logger.Error($" Failed to find {search} with error:\n{e.ToString()}");
				}
			}
			return null;
		}

		// TTQMM Ref JsonToGameObject.MakeJSONArray(object originalArray, Type ArrayType, JArray Deserialize, string Spacing, bool Wipe)
		private static object MakeJSONArray(object originalArray, Type arrayType, JArray jArray, bool wipe)
		{
			IList newList;
			IList sourceList = wipe ? null : originalArray as IList;

			// If the target is a JToken array, then we are basically done
			if (arrayType == kTypeJToken)
				return jArray;

			Type itemType;
			if (arrayType.IsGenericType)
				itemType = arrayType.GetGenericArguments()[0];
			else
				itemType = arrayType.GetElementType();

			int count = jArray.Count;
			try
			{
				// newCount here tells fixed arrays how many items to have. List<> arrays get starting capacity, but is empty.
				newList = Activator.CreateInstance(arrayType, count) as IList;

				// Must be a List<> then, which means it can be expanded with the following...
				while (newList.Count < count)
				{
					object def = itemType.IsClass ? null : Activator.CreateInstance(itemType); // Get default (Avoid creation if not needed)
					newList.Add(def); // Populate empty list from 0 to length
				}

				// Populate the list from our JSON
				for (int i = 0; i < count; i++) 
				{
					// WP: Do not reference the original object! (Corruption risk)
					object element = newList[i]; 

					if (jArray[i] is JObject jObject)
					{
						// Make an element if we don't have one
						if (element == null)
						{
							element = Activator.CreateInstance(itemType); // Create instance, because is needed
							if (sourceList != null && sourceList.Count != 0) // Copy current or last element
							{
								ShallowCopy(itemType, sourceList[Math.Min(i, sourceList.Count - 1)], element, true); // WP: Helpful, trust me
							}
						}
						// Then deserialize into that element
						DeserializeJSONObject(element, itemType, jObject);
					}
					else if (jArray[i] is JArray jSubArray)
					{
						element = MakeJSONArray(element, itemType, jSubArray, false);
					}
					else if (jArray[i] is JValue jValue)
					{
						try
						{
							element = jValue.ToObject(itemType);
						}
						catch
						{
							string cache = jValue.ToObject<string>();
							string targetName = cache.Substring(cache.IndexOf('|') + 1);
							element = DeserializeValueReference(targetName, cache, itemType);
						}
					}
					newList[i] = element;
				}

				return newList;
			}
			catch(Exception)
			{
				return null;
			}
		}

		private static Type[] kForceInstantiateObjectTypes = new Type[]
		{
			typeof(TireProperties),
			typeof(ManWheels.TireProperties)
		};

		// TODO: Do we have to have this??
		internal static Transform sCurrentSearchTransform = null;
		private static Stack<Transform> sTransformSearchStack = new Stack<Transform>();
		private static Transform GetRootSearchTransform()
		{
			NuterraMod.logger.Debug($"Current root search transform is {sCurrentSearchTransform}");
			return sCurrentSearchTransform;
		}
		private static Transform GetCurrentSearchTransform() {
			Transform transform = sTransformSearchStack.Peek();
			NuterraMod.logger.Debug($"Current search transform is {transform}");
			return transform;
		}
		private static void PushSearchTransform(Transform t) {
			NuterraMod.logger.Trace($"[STACK] PUSH transform {t}");
			sTransformSearchStack.Push(t);
		}
		private static void PopSearchTransform()
		{
			Transform removed = sTransformSearchStack.Pop();
			NuterraMod.logger.Trace($"[STACK] POP transform {removed}");
		}

		// TTQMM Ref : GameObjectJSON.SetJSONObject(JObject jObject, object instance, string Spacing, bool Wipe, bool Instantiate, FieldInfo tField, PropertyInfo tProp, bool UseField)
		private static void SetJSONObject(JObject jObject, object target, bool wipe, bool instantiate, FieldInfo tField, PropertyInfo tProp, bool UseField)
		{
			if (UseField)
			{
				object rewrite = SetJSONObject_Internal(jObject, wipe, instantiate, wipe ? null : tField.GetValue(target), tField.FieldType, tField.Name);
				try {
					NuterraMod.logger.Debug($"Setting value of field {tField.Name}");
					tField.SetValue(target, rewrite);
				}
				catch (Exception E)
				{
					NuterraMod.logger.Error($"Failed to set JSON object as value of field {tField.Name}\n" + E.ToString());
				}
			}
			else
			{
				object rewrite = SetJSONObject_Internal(jObject, wipe, instantiate, wipe || !tProp.CanRead ? null : tProp.GetValue(target, null), tProp.PropertyType, tProp.Name);
				if (tProp.CanWrite)
				{
					try
					{
						NuterraMod.logger.Debug($"Setting value of property {tProp.Name}");
						tProp.SetValue(target, rewrite, null);
					}
					catch (Exception E)
					{
						NuterraMod.logger.Error($"Failed to set JSON object as value of property {tProp.Name}\n" + E.ToString());
					}
				}
				else
                {
					NuterraMod.logger.Warn($"Property {tProp.Name} on type {tProp.DeclaringType} is not writeable");
                }
			}
		}

		// TTQMM Ref: GameObjectJSON.SetJSONObject_Internal(JObject jObject, string Spacing, bool Wipe, bool Instantiate, object original, Type type, string name)
		private static object SetJSONObject_Internal(JObject jObject, bool wipe, bool instantiate, object original, Type originalType, string name)
		{
			NuterraMod.logger.Debug($"Setting field/property {name} value (type {originalType})");
			object rewrite = null;

			// First point of order, some types have to be instantiated
			if (kForceInstantiateObjectTypes.Contains(originalType))
				instantiate = true;

			bool isGO = originalType.IsAssignableFrom(kTypeGameObject);
			bool isTransform = originalType.IsAssignableFrom(kTypeTransform);
			bool isComponent = originalType.IsSubclassOf(kTypeComponent);

			// If wipe or we have nothing to start with
			if (wipe || original == null)
			{
				if (isGO || isTransform) // UnityEngine.Component (Module)
				{
					// Instantiate the original object
					GameObject originalObject = null;
					if (isGO)
						originalObject = original as GameObject;
					if(isTransform)
						originalObject = (original as Transform).gameObject;
					GameObject newObject = GameObject.Instantiate(originalObject);

					// Initialise its transforms
					newObject.SetActive(false);
					newObject.transform.parent = originalObject.transform.parent;
					newObject.transform.position = Vector3.down * 25000f; // What? Bye bye transform?!

					DeserializeIntoGameObject(jObject, newObject.gameObject);
					
					if (isGO)
					{
						if (wipe && original != null)
							GameObject.DestroyImmediate(original as GameObject);
						rewrite = newObject;
					}
					else
					{
						if (wipe && original != null)
							GameObject.DestroyImmediate(original as Component);
						rewrite = newObject.GetComponent(originalType);
					}
				}
				else // Something other than a GameObject or Transform
				{
					// Create an instance with new() and deserialize our JSON into it
					try
					{
						if (typeof(ScriptableObject).IsAssignableFrom(originalType))
						{
							NuterraMod.logger.Debug($"Detected {originalType} is ScriptableObject");
							original = ScriptableObject.CreateInstance(originalType);
						}
						else
						{
							NuterraMod.logger.Debug($"{originalType} is NOT ScriptableObject");
							original = Activator.CreateInstance(originalType);
						}
					}
					catch
					{
						// We can try seeing if our parameters fit any of the other constructors
						foreach (ConstructorInfo constructor in originalType.GetConstructors())
						{
							try
							{
								// Look for constructors of that fit
								ParameterInfo[] parameters = constructor.GetParameters();
								object[] values = new object[parameters.Length];
								for (int i = 0; i < parameters.Length; i++)
								{
									if (jObject.TryGetValue(parameters[i].Name, out JToken jValue))
										values[i] = jValue.ToObject(parameters[i].ParameterType);
									else if (parameters[i].HasDefaultValue)
										values[i] = parameters[i].DefaultValue;
									else
										values[i] = null;
								}
								original = constructor.Invoke(values);
								break;
							}
							catch
							{
								NuterraMod.logger.Warn($" Failed to match constructor for {originalType}");
							}
						}
					}
					if (original != null)
						rewrite = DeserializeJSONObject(original, originalType, jObject);
					else
						NuterraMod.logger.Error($" Failed to create instance of {originalType}");
				}
			}
			else // We are not wiping the source and we have a reference original
			{
				if (instantiate)
				{
					if (isGO || isComponent)
					{
						GameObject originalObject = (original as Component).gameObject;
						GameObject newObject = GameObject.Instantiate(originalObject);
	
						newObject.SetActive(false);
						newObject.transform.parent = originalObject.transform.parent;
						newObject.transform.position = Vector3.down * 25000f;

						DeserializeIntoGameObject(jObject, newObject.gameObject);

						if (isGO)
						{
							rewrite = newObject;
						}
						else
						{
							rewrite = newObject.GetComponent(originalType);
							NuterraMod.logger.Debug($"Finished deserializing data into component {(rewrite as Component).name}");
						}
					}
					else // Some data structure, not extending Component
					{
						object newObj = null;
						try
						{
							newObj = Activator.CreateInstance(originalType);
						}
						catch
						{
							// We can try seeing if our parameters fit any of the other constructors
							foreach (ConstructorInfo constructor in originalType.GetConstructors())
							{
								try
								{
									// Look for constructors of that fit
									ParameterInfo[] parameters = constructor.GetParameters();
									object[] values = new object[parameters.Length];
									for (int i = 0; i < parameters.Length; i++)
									{
										if (jObject.TryGetValue(parameters[i].Name, out JToken jValue))
											values[i] = jValue.ToObject(parameters[i].ParameterType);
										else if (parameters[i].HasDefaultValue)
											values[i] = parameters[i].DefaultValue;
										else
											values[i] = null;
									}
									original = constructor.Invoke(values);
									break;
								}
								catch
								{
									NuterraMod.logger.Warn($" Failed to match constructor for {originalType}");
								}
							}
						}

						ShallowCopy(originalType, original, newObj, true);

						if (newObj != null)
							rewrite = DeserializeJSONObject(newObj, originalType, jObject);
						else
							NuterraMod.logger.Error($" Failed to create instance of {originalType}");
					}
				}
				else // !instantiate
				{
					rewrite = DeserializeJSONObject(original, originalType, jObject);
				}
			}

			return rewrite;
		}

		// -----------------------------------------------------------------------------------
		#region Copy, Deserialize, Serialize Helpers
		// -----------------------------------------------------------------------------------


		public static void ShallowCopy(Type sharedType, object source, object target, bool declaredVarsOnly)
		{
			BindingFlags bf = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
			if (declaredVarsOnly)
				bf |= BindingFlags.DeclaredOnly;
			var fields = sharedType.GetFields(bf);
			foreach (var field in fields)
			{
				try
				{
					field.SetValue(target, field.GetValue(source));
				}
				catch { }
			}
			var props = sharedType.GetProperties(bf);
			foreach (var prop in props)
			{
				try
				{
					if (prop.CanRead && prop.CanWrite)
						prop.SetValue(target, prop.GetValue(source), null);
				}
				catch { }
			}
		}

		public static void ShallowCopy(Type sharedType, object source, object target, string[] filter)
		{
			var bf = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
			foreach (string search in filter)
			{
				var field = sharedType.GetField(search, bf);
				if (field != null)
				{
					try
					{
						field.SetValue(target, field.GetValue(source));
					}
					catch { }
				}
				else
				{
					var prop = sharedType.GetProperty(search, bf);
					if (prop != null)
					{
						try
						{
							if (prop.CanRead && prop.CanWrite)
								prop.SetValue(target, prop.GetValue(source), null);
						}
						catch { }
					}
				}
			}
		}
		#endregion
		// -----------------------------------------------------------------------------------
	}
}
